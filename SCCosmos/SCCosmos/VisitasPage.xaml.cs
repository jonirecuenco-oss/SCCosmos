using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SCCosmos
{
    public partial class VisitasPage : Page
    {
        private string _congreCodigo;
        private string _congreNombre;
        private int _totalPublicadoresActivos = 0;

        public static string CodigoPendiente = "";
        public static string NombrePendiente = "";
        public static string MesPendiente = "";

        private bool _cargandoDatos = false;
        // Territorios se carga de forma independiente de Ministerio y del resto
        // de módulos. Usar la bandera global aquí hacía que no se cargase al abrir
        // la página si Ministerio seguía procesando datos en segundo plano.
        private bool _cargandoTerritorios = false;

        private List<FilaVisitaPublisher> _filasVisitasGlobal = new List<FilaVisitaPublisher>();
        private string _busquedaMinisterio = "";

        // La búsqueda espera a que se deje de escribir (como en Personas).
        private DispatcherTimer _timerBusquedaMinisterio;

        // Personas sin actividad informada en los últimos 6 meses (por su
        // id raíz). Se excluyen de los contadores y de las estadísticas.
        private HashSet<int> _idsInactivos = new HashSet<int>();

        /// <summary>Grupo de predicación con su número de personas.</summary>
        public sealed class GrupoVisitaUI
        {
            public string Nombre { get; set; } = "";
            public int Cantidad { get; set; }

            // Bloque final con los publicadores inactivos.
            public bool EsInactivos { get; set; }

            public override string ToString() =>
                Cantidad > 0 ? $"{Nombre} ({Cantidad})" : Nombre;
        }
        private List<FilaTablaTerritorio> _territoriosGlobal = new List<FilaTablaTerritorio>();
        private int _paginaActual = 1;
        private const int _itemsPorPagina = 20;

        // Filtro de las tarjetas-resumen de Territorios. Solo puede haber
        // uno activo a la vez. Al desactivarlo recuperamos la página anterior.
        private string _filtroTerritoriosActivo = "";
        private int _paginaAntesFiltroTerritorios = 1;

        // Variables de estado independientes para cada módulo
        private DateTime _mesMinisterio;
        private DateTime _mesPrograma;
        private DateTime _mesNotas;
        private DateTime _mesTerritorios;
        private DateTime _mesContabilidad;
        private DateTime _mesS303;

        private bool _mapaCargado = false;
        private string _htmlMapaCache = "";

        // Carta generada para esta congregación/mes y selección de aprobaciones.
        // Se abre una COPIA editable: nunca se modifica la plantilla de Correspondencia.
        private string _cartaNombramientosEditable;
        private string _firmaCartaNombramientos;

        public List<CategoriaNotas> MisNotasAgrupadas { get; set; }

        public VisitasPage()
        {
            InitializeComponent();
            AplicarPerfilAccesoVisita();

            _timerBusquedaMinisterio = new DispatcherTimer();
            _timerBusquedaMinisterio.Interval = TimeSpan.FromMilliseconds(400);
            _timerBusquedaMinisterio.Tick += TimerBusquedaMinisterio_Tick;

            this.Loaded += Page_Loaded;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CodigoPendiente)) return;

            if (PanelCargando != null) PanelCargando.Visibility = Visibility.Visible;
            await Task.Delay(50);

            _congreCodigo = CodigoPendiente;
            _congreNombre = NombrePendiente;
            TxtTitulo.Text = $"VISITA - {_congreNombre}";

            InicializarMeses();
            CargarS303yS2();
            AjustarVisibilidadColumnas(this.ActualWidth);

            if (PanelCargando != null) PanelCargando.Visibility = Visibility.Collapsed;
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AjustarVisibilidadColumnas(e.NewSize.Width);

            if (TabsVisita != null && ReferenceEquals(TabsVisita.SelectedItem, TabMapa))
            {
                AjustarAlturaMapaSegura(e.NewSize.Height);
            }
        }

        private void AjustarVisibilidadColumnas(double anchoPantalla)
        {
            if (TablaVisitas == null || TablaVisitas.Columns.Count == 0) return;

            bool perfilLimitado = !PerfilAcceso.AccesoCompleto;

            foreach (var col in TablaVisitas.Columns)
            {
                string header = col.Header?.ToString();
                if (string.IsNullOrEmpty(header)) continue;

                // La esposa del SC no ve los cuadros de las horas ni los
                // territorios trabajados.
                if (perfilLimitado && EsColumnaDeHoras(header)) { col.Visibility = Visibility.Collapsed; continue; }
                if (perfilLimitado && header == "Terrs.") { col.Visibility = Visibility.Collapsed; continue; }

                if (header == "Nombre JW Hub") col.Visibility = anchoPantalla < 1250 ? Visibility.Collapsed : Visibility.Visible;
                else if (header == "Sexo" || header == "Edad" || header == "Baut.") col.Visibility = anchoPantalla < 1100 ? Visibility.Collapsed : Visibility.Visible;
                else if (header == "Terrs." || header == "CBs") col.Visibility = anchoPantalla < 950 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>
        /// Las columnas de los 6 meses se titulan "May 26", "Jun 26"...
        /// </summary>
        private static bool EsColumnaDeHoras(string header)
        {
            return header.Length == 6
                && header[3] == ' '
                && header.Substring(4).All(char.IsDigit);
        }

        /// <summary>
        /// La esposa del SC solo ve la información de la congregación, el
        /// programa, los grupos y el ministerio sin los cuadros de las horas.
        /// </summary>
        private void AplicarPerfilAccesoVisita()
        {
            if (PerfilAcceso.AccesoCompleto)
            {
                return;
            }

            TabMapa.Visibility = Visibility.Collapsed;
            TabNotas.Visibility = Visibility.Collapsed;
            TabTerritorios.Visibility = Visibility.Collapsed;
            TabContabilidad.Visibility = Visibility.Collapsed;
            TabS303.Visibility = Visibility.Collapsed;

            // Tampoco ve el selector de meses del Ministerio.
            CintaMesMinisterio.Visibility = Visibility.Collapsed;
        }

        private void UIElement_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender };
                if (MainScrollViewer != null) MainScrollViewer.RaiseEvent(eventArg);
            }
        }

        /// <summary>
        /// Último mes que se considera «informado» por la congregación.
        ///
        /// No basta con que UNA persona tenga actividad: un mes solo cuenta
        /// si lo informó una parte significativa de los publicadores (al
        /// menos el 10 %, con un mínimo de 2). Así una fila suelta o mal
        /// importada no hace aparecer un mes vacío al final de la tabla.
        /// </summary>
        private DateTime CalcularMaximoMesActividad()
        {
            var conteo = DatabaseHelper.ObtenerConteoActividadPorMes(_congreCodigo);

            if (conteo.Count == 0)
            {
                return DateTime.MinValue;
            }

            int activos = DatabaseHelper.ObtenerNumeroPublicadoresActivos(_congreCodigo);
            int minimo = Math.Max(2, (int)Math.Ceiling(activos * 0.10));

            DateTime maximo = DateTime.MinValue;
            bool algunoValido = false;

            foreach (var par in conteo)
            {
                if (par.Value < minimo)
                {
                    continue;
                }

                algunoValido = true;

                if (par.Key > maximo)
                {
                    maximo = par.Key;
                }
            }

            // Si ningún mes llega al umbral (congregaciones muy pequeñas o
            // datos incompletos), se usa el último mes con cualquier dato.
            if (!algunoValido)
            {
                foreach (var par in conteo)
                {
                    if (par.Key > maximo)
                    {
                        maximo = par.Key;
                    }
                }
            }

            return maximo;
        }

        // ==========================================
        // CINTAS DE MESES INDEPENDIENTES (RIBBONS)
        // ==========================================

        private void InicializarMeses()
        {
            DateTime mesBase = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            if (!string.IsNullOrEmpty(MesPendiente) && DateTime.TryParseExact(MesPendiente, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedMes))
            {
                mesBase = parsedMes;
            }

            _mesMinisterio = mesBase;
            _mesPrograma = mesBase;
            _mesNotas = mesBase;
            _mesTerritorios = mesBase;
            _mesContabilidad = mesBase;
            _mesS303 = mesBase;

            ActualizarUI_MesMinisterio();
            ActualizarUI_MesPrograma();
            ActualizarUI_MesNotas();
            ActualizarUI_MesTerritorios();
            ActualizarUI_MesContabilidad();
            ActualizarUI_MesS303();
        }

        private string FormatearMes(DateTime d)
        {
            string nombreMes = d.ToString("MMMM yyyy", new CultureInfo("es-ES"));
            return char.ToUpper(nombreMes[0]) + nombreMes.Substring(1);
        }

        // --- MINISTERIO ---
        private async void ActualizarUI_MesMinisterio()
        {
            if (TxtMesMinisterio != null) TxtMesMinisterio.Text = FormatearMes(_mesMinisterio);
            if (_cargandoDatos) return;
            _cargandoDatos = true;
            try
            {
                DateTime maxMesAct = CalcularMaximoMesActividad();
                DateTime mesFinGrid = (maxMesAct != DateTime.MinValue && _mesMinisterio > maxMesAct) ? maxMesAct : _mesMinisterio;

                DateTime[] mesesARenderizar = new DateTime[6];
                for (int i = 0; i < 6; i++) mesesARenderizar[5 - i] = mesFinGrid.AddMonths(-i);

                var listadoHermanosHistorico = DatabaseHelper.ObtenerPersonasHistoricasPorMes(_congreCodigo, _mesMinisterio.ToString("yyyy-MM"));

                var relojMinisterio = System.Diagnostics.Stopwatch.StartNew();

                await ProcesarColumnasYDatosDinamicos(mesesARenderizar, listadoHermanosHistorico);

                Perf.Registrar("Visitas.Ministerio", relojMinisterio.ElapsedMilliseconds);

                CargarGruposPredicacion(listadoHermanosHistorico);
            }
            finally { _cargandoDatos = false; }
        }

        private void BtnMesAntMinisterio_Click(object sender, RoutedEventArgs e) { _mesMinisterio = _mesMinisterio.AddMonths(-1); ActualizarUI_MesMinisterio(); }
        private void BtnMesSigMinisterio_Click(object sender, RoutedEventArgs e) { _mesMinisterio = _mesMinisterio.AddMonths(1); ActualizarUI_MesMinisterio(); }

        // --- PROGRAMA ---
        private void ActualizarUI_MesPrograma()
        {
            // Programa es un módulo independiente. No debe quedar bloqueado
            // mientras otro módulo (por ejemplo Ministerio) está cargando.
            CargarPrograma(_mesPrograma);
        }

        // --- NOTAS ---
        private void ActualizarUI_MesNotas()
        {
            // Notas es un módulo independiente. No debe quedar bloqueado
            // por la bandera global usada durante otras cargas asíncronas.
            CargarNotas(_mesNotas);
        }

        // --- TERRITORIOS / AÑO DE SERVICIO ---
        private async void ActualizarUI_MesTerritorios()
        {
            // Territorios no debe depender de la carga de Ministerio.
            // Solo evitamos que se lancen dos cargas de Territorios a la vez.
            if (_cargandoTerritorios) return;

            _cargandoTerritorios = true;

            try
            {
                int anioServicio =
                    ObtenerAnioServicio(
                        _mesTerritorios);

                DateTime inicioAnioServicio =
                    new DateTime(
                        anioServicio - 1,
                        9,
                        1);

                DateTime finAnioServicio =
                    new DateTime(
                        anioServicio,
                        8,
                        31);

                if (TxtMesTerritorios != null)
                {
                    TxtMesTerritorios.Text =
                        $"Año de servicio {anioServicio}";
                }

                // El selector de Territorios debe partir del MISMO conjunto
                // de personas que "MINISTERIO Y PUBLICADORES".
                // Los nombres visibles se hidratan después desde la ficha raíz actual.
                var listadoHermanosReferencia =
                    DatabaseHelper
                        .ObtenerPersonasHistoricasPorMes(
                            _congreCodigo,
                            _mesMinisterio.ToString(
                                "yyyy-MM"));

                await CargarTerritorios(
                    anioServicio,
                    inicioAnioServicio,
                    finAnioServicio,
                    listadoHermanosReferencia);
            }
            finally
            {
                _cargandoTerritorios = false;
            }
        }

        private static int ObtenerAnioServicio(
            DateTime fecha)
        {
            // El año de servicio termina en agosto del año indicado.
            // Ejemplo: año de servicio 2026 = 01/09/2025 a 31/08/2026.
            return
                fecha.Month >= 9
                    ? fecha.Year + 1
                    : fecha.Year;
        }

        private static int ObtenerNumeroTerritorio(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return int.MaxValue;

            // Orden natural: 1, 2, 3, ... 10, 11... aunque el nombre
            // contenga texto (por ejemplo "Territorio 10").
            var match = System.Text.RegularExpressions.Regex.Match(nombre, @"\d+");
            if (match.Success && int.TryParse(match.Value, out int numero))
            {
                return numero;
            }

            // Los nombres sin número se muestran después de los numerados.
            return int.MaxValue;
        }

        private void BtnMesAntTerritorios_Click(
            object sender,
            RoutedEventArgs e)
        {
            _mesTerritorios =
                _mesTerritorios.AddYears(-1);

            ActualizarUI_MesTerritorios();
        }

        private void BtnMesSigTerritorios_Click(
            object sender,
            RoutedEventArgs e)
        {
            _mesTerritorios =
                _mesTerritorios.AddYears(1);

            ActualizarUI_MesTerritorios();
        }

        private void BtnImportarTerritoriosJson_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_congreCodigo))
            {
                MessageBox.Show(
                    "No se ha podido identificar la congregación.",
                    "Territorios",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var dialogo = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Archivos JSON (*.json)|*.json",
                Title = "Seleccionar S-13 (territorios)"
            };

            if (dialogo.ShowDialog() != true)
            {
                return;
            }

            try
            {
                string contenido =
                    System.IO.File
                        .ReadAllText(dialogo.FileName)
                        .Trim();

                if (!contenido.StartsWith("["))
                {
                    contenido = "[" + contenido + "]";
                }

                var opciones = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var lista = JsonSerializer
                    .Deserialize<List<TerritorioJSON>>(
                        contenido,
                        opciones);

                if (lista == null || lista.Count == 0)
                {
                    MessageBox.Show(
                        "El archivo no contiene territorios.",
                        "Territorios",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                ImportarTerritoriosDesdeLista(lista);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo importar el JSON:\n\n" +
                    ex.Message,
                    "Territorios",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// Importa el registro de territorios directamente desde el PDF
        /// o el Excel del S-13 (sin pasar por el JSON).
        /// </summary>
        private async void BtnImportarTerritoriosPdf_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_congreCodigo))
            {
                MessageBox.Show(
                    "No se ha podido identificar la congregación.",
                    "Territorios",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var dialogo = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Registro de territorios (*.pdf;*.xlsx)|*.pdf;*.xlsx|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar el registro de territorios (S-13)"
            };

            if (dialogo.ShowDialog() != true)
            {
                return;
            }

            List<SCCosmos.Sincro.TerritorioS13> convertidos = null;
            string error = null;

            await Task.Run(() =>
            {
                try
                {
                    convertidos = SCCosmos.Sincro.ConvertidorS13
                        .ConvertirArchivo(dialogo.FileName);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });

            if (error != null)
            {
                MessageBox.Show(
                    "No se pudo leer el archivo:\n\n" + error,
                    "Territorios",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            if (convertidos == null || convertidos.Count == 0)
            {
                MessageBox.Show(
                    "No se reconocieron territorios en ese archivo.",
                    "Territorios",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // Se guarda el JSON junto al archivo (útil para revisarlo).
            try
            {
                string carpeta = System.IO.Path.GetDirectoryName(dialogo.FileName);
                SCCosmos.Sincro.ConvertidorS13.GuardarJson(convertidos, carpeta);
            }
            catch
            {
            }

            var lista = convertidos
                .Select(t => new TerritorioJSON
                {
                    Nombre = t.Nombre,
                    UltimaFechaCompletado = t.UltimaFechaCompletado,
                    AsignadoA = t.AsignadoA,
                    FechaAsignado = t.FechaAsignado,
                    FechaCompletado = t.FechaCompletado
                })
                .ToList();

            ImportarTerritoriosDesdeLista(lista);
        }


        private void ImportarTerritoriosDesdeLista(List<TerritorioJSON> lista)
        {
            try
            {
                int existentes = DatabaseHelper
                    .ObtenerTerritoriosPorCongregacion(
                        _congreCodigo)
                    .GroupBy(t => t.Nombre)
                    .Count();

                if (existentes > 0)
                {
                    var respuesta = MessageBox.Show(
                        "Esta congregación ya tiene " + existentes +
                        " territorios. Se sustituirán por los " +
                        lista.Count + " del archivo.\n\n¿Continuar?",
                        "Importar S-13",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (respuesta != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                DatabaseHelper.GuardarTerritoriosImportados(
                    _congreCodigo,
                    lista);

                ActualizarUI_MesTerritorios();

                MessageBox.Show(
                    "Se importaron " + lista.Count +
                    " territorios del S-13.",
                    "Territorios",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo importar el JSON:\n\n" +
                    ex.Message,
                    "Territorios",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // --- CONTABILIDAD ---
        private void ActualizarUI_MesContabilidad()
        {
            int anioServicio = ObtenerAnioServicio(_mesContabilidad);

            if (TxtMesContabilidad != null)
            {
                TxtMesContabilidad.Text = $"Año de servicio {anioServicio}";
            }

            // Contabilidad es independiente de la carga asíncrona de Ministerio.
            // Antes se bloqueaba con _cargandoDatos y, al abrir VISITAS, el título
            // del año se actualizaba pero la tabla quedaba vacía hasta cambiar de año.
            CargarContabilidadList(_mesContabilidad);
        }

        private void BtnMesAntContabilidad_Click(object sender, RoutedEventArgs e)
        {
            _mesContabilidad = _mesContabilidad.AddYears(-1);
            ActualizarUI_MesContabilidad();
        }

        private void BtnMesSigContabilidad_Click(object sender, RoutedEventArgs e)
        {
            _mesContabilidad = _mesContabilidad.AddYears(1);
            ActualizarUI_MesContabilidad();
        }

        // --- S-303 ---
        private void ActualizarUI_MesS303()
        {
            if (!_cargandoDatos) CargarS303yS2();
        }

        // ==========================================
        // CREADORES DE MÓDULOS (PROGRAMA Y NOTAS)
        // ==========================================
        /// <summary>Abre el evento del programa en Google Maps.</summary>
        private void BtnMapaActividad_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var activo =
                    (sender as System.Windows.FrameworkElement)?.Tag
                    as BotonActividadUI;

                if (activo == null)
                {
                    return;
                }

                string consulta =
                    (activo.ActividadDB?.Direccion ?? "").Trim();

                if (string.IsNullOrWhiteSpace(consulta))
                {
                    string ubicacion =
                        (activo.Ubicacion ?? "").Trim();

                    if (ubicacion.Equals(
                            "Sin ubicación",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        // Sin dirección propia: la del salón del circuito.
                        try
                        {
                            consulta =
                                DatabaseHelper
                                    .ObtenerCongregaciones()
                                    .FirstOrDefault(c => string.Equals(
                                        c.Codigo,
                                        _congreCodigo,
                                        StringComparison.OrdinalIgnoreCase))
                                    ?.DireccionSR
                                    ?.Trim()
                                ?? "";
                        }
                        catch
                        {
                            consulta = "";
                        }
                    }
                    else
                    {
                        // Quita el icono del pin si lo lleva delante.
                        int espacio =
                            ubicacion.IndexOf(' ');

                        consulta =
                            espacio >= 0
                                ? ubicacion.Substring(espacio + 1).Trim()
                                : ubicacion;
                    }
                }

                if (string.IsNullOrWhiteSpace(consulta))
                {
                    return;
                }

                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(
                        "https://www.google.com/maps/search/?api=1&query="
                        + Uri.EscapeDataString(consulta))
                    {
                        UseShellExecute = true
                    });
            }
            catch
            {
            }
        }


        /// <summary>Nombre de pila para la insignia (vacío si es común).</summary>
        private static string NombreCortoResponsable(string responsable)
        {
            string valor =
                (responsable ?? "").Trim();

            if (string.Equals(valor, "SC", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseHelper
                    .ObtenerConfiguracion("NombreSC", "")
                    .Trim();
            }

            if (string.Equals(valor, "Esposa", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseHelper
                    .ObtenerConfiguracion("NombreEsposaSC", "")
                    .Trim();
            }

            return "";
        }


        /// <summary>
        /// Importa el programa de la visita desde el PDF rellenable
        /// («Programa para la visita del SC») y sustituye el programa del
        /// mes por los eventos leídos.
        /// </summary>
        private void BtnImportarProgramaPdf_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialogo = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importar programa de la visita (PDF)",
                Filter = "Archivos PDF (*.pdf)|*.pdf",
                Multiselect = false
            };

            if (dialogo.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var eventos =
                    SCCosmos.Sincro.ImportadorProgramaVisita
                        .LeerPdf(dialogo.FileName);

                // Las reuniones y las salidas de predicación son en el salón
                // del Reino: se les pone su dirección como ubicación.
                try
                {
                    string direccionSalon =
                        DatabaseHelper
                            .ObtenerCongregaciones()
                            .FirstOrDefault(c =>
                                string.Equals(
                                    c.Codigo,
                                    _congreCodigo,
                                    StringComparison.OrdinalIgnoreCase))
                            ?.DireccionSR
                            ?.Trim()
                        ?? "";

                    if (!string.IsNullOrWhiteSpace(direccionSalon))
                    {
                        foreach (var evento in eventos)
                        {
                            bool esReunion =
                                (evento.TipoActividad ?? "")
                                    .StartsWith(
                                        "Reunión",
                                        StringComparison.OrdinalIgnoreCase);

                            bool esViernes =
                                string.Equals(
                                    evento.DiaSemana,
                                    "Viernes",
                                    StringComparison.OrdinalIgnoreCase)
                                && string.Equals(
                                    evento.Turno,
                                    "General",
                                    StringComparison.OrdinalIgnoreCase);

                            bool esPredicacion =
                                string.Equals(
                                    evento.TipoActividad,
                                    "Predicación",
                                    StringComparison.OrdinalIgnoreCase);

                            if (esReunion || esViernes || esPredicacion)
                            {
                                evento.Ubicacion =
                                    direccionSalon;
                            }
                        }
                    }
                }
                catch
                {
                }

                if (eventos.Count == 0)
                {
                    MessageBox.Show(
                        "No se han encontrado eventos en el PDF. Comprueba que es el formulario «Programa para la visita del SC» ya rellenado.",
                        "Importar programa",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                string resumen =
                    string.Join(
                        Environment.NewLine,
                        eventos
                            .Take(12)
                            .Select(v =>
                                "· " + v.DiaSemana + " " + v.Hora
                                + " — " + v.TipoActividad
                                + (string.IsNullOrWhiteSpace(v.Responsable)
                                    ? ""
                                    : " (" + v.Responsable + ")")));

                if (eventos.Count > 12)
                {
                    resumen +=
                        Environment.NewLine
                        + $"… y {eventos.Count - 12} más.";
                }

                var respuesta =
                    MessageBox.Show(
                        $"Se han leído {eventos.Count} eventos del programa:"
                        + Environment.NewLine + Environment.NewLine
                        + resumen
                        + Environment.NewLine + Environment.NewLine
                        + "Se sustituirá el programa de este mes por estos eventos. ¿Continuar?",
                        "Importar programa de la visita",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (respuesta != MessageBoxResult.Yes)
                {
                    return;
                }

                int creados =
                    DatabaseHelper.ReemplazarProgramaMesVisita(
                        _congreCodigo,
                        _mesPrograma.ToString("yyyy-MM"),
                        eventos);

                MessageBox.Show(
                    $"Importado: {creados} eventos en el programa de la visita.",
                    "Importar programa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                try
                {
                    CargarPrograma(
                        _mesPrograma);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo importar el programa:\n" + ex.Message,
                    "Importar programa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private void CargarPrograma(DateTime mesReferencia)
        {
            string mesRefStr = mesReferencia.ToString("yyyy-MM");

            var actividadesDB =
                DatabaseHelper.ObtenerActividadesProgramaMes(
                    _congreCodigo,
                    mesRefStr);

            string[] dias =
            {
                "Martes",
                "Miércoles",
                "Jueves",
                "Viernes",
                "Sábado",
                "Domingo"
            };

            var diasPrograma = new List<DiaProgramaUI>();

            foreach (string dia in dias)
            {
                var actividadesDia =
                    actividadesDB
                        .Where(a =>
                            string.Equals(
                                a.DiaSemana?.Trim(),
                                dia,
                                StringComparison.OrdinalIgnoreCase))
                        .OrderBy(a => ObtenerOrdenHoraPrograma(a.Hora))
                        .ThenBy(a => a.Id)
                        .Select(a =>
                        {
                            Persona acompanante =
                                ObtenerPersonaActualParaPrograma(
                                    a.CompaneroId);

                            string nombreAcompanante =
                                ObtenerNombrePreferidoPrograma(
                                    acompanante);

                            string tipo =
                                a.TipoActividad?.Trim() ?? "";

                            // Compatibilidad con el programa antiguo:
                            // Comida/Cenar/Dormir estaban guardados como "Responsable".
                            if (string.IsNullOrWhiteSpace(tipo)
                                &&
                                (
                                    string.Equals(a.Responsable, "Comida", StringComparison.OrdinalIgnoreCase)
                                    ||
                                    string.Equals(a.Responsable, "Cenar", StringComparison.OrdinalIgnoreCase)
                                    ||
                                    string.Equals(a.Responsable, "Dormir", StringComparison.OrdinalIgnoreCase)
                                ))
                            {
                                tipo = a.Responsable;
                            }

                            if (string.IsNullOrWhiteSpace(tipo))
                            {
                                tipo = "Actividad";
                            }

                            string responsable =
                                NombreCortoResponsable(
                                    NormalizarResponsablePrograma(
                                        a.Responsable));

                            return new BotonActividadUI
                            {
                                Hora =
                                    string.IsNullOrWhiteSpace(a.Hora)
                                        ? "--:--"
                                        : a.Hora.Trim(),

                                TipoActividad = tipo,
                                Acompanante =
                                    string.IsNullOrWhiteSpace(nombreAcompanante)
                                        ? "Sin acompañante"
                                        : nombreAcompanante,

                                Ubicacion =
                                    string.IsNullOrWhiteSpace(a.Direccion)
                                        ? "Sin ubicación"
                                        : "📍 " + a.Direccion.Trim(),

                                Notas =
                                    a.NotasLibro?.Trim() ?? "",

                                Dia = dia,
                                Turno = "General",
                                Responsable = responsable,
                                ActividadDB = a
                            };
                        })
                        .ToList();

                diasPrograma.Add(
                    new DiaProgramaUI
                    {
                        Dia = dia,
                        Actividades = actividadesDia
                    });
            }

            ListaProgramaDias.ItemsSource = diasPrograma;

        }

        private Persona ObtenerPersonaActualParaPrograma(int personaId)
        {
            if (personaId <= 0)
            {
                return null;
            }

            Persona persona =
                DatabaseHelper.ObtenerPersonaPorId(
                    personaId);

            if (persona != null
                &&
                persona.PersonaRaizId > 0)
            {
                Persona raiz =
                    DatabaseHelper.ObtenerPersonaPorId(
                        persona.PersonaRaizId);

                if (raiz != null)
                {
                    persona = raiz;
                }
            }

            return persona;
        }

        private static string ObtenerNombrePreferidoPrograma(Persona persona)
        {
            if (persona == null)
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(persona.NombreComun))
            {
                return persona.NombreComun.Trim();
            }

            if (!string.IsNullOrWhiteSpace(persona.NombreJWHub))
            {
                return persona.NombreJWHub.Trim();
            }

            return persona.Nombre?.Trim() ?? "";
        }

        private static string NormalizarResponsablePrograma(string responsable)
        {
            string valor =
                responsable?.Trim() ?? "";

            if (string.Equals(
                    valor,
                    "Ambos",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Ambos";
            }

            if (string.Equals(
                    valor,
                    "Rebe",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    valor,
                    "Esposa",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Esposa";
            }

            // En el programa anterior había registros de Comida/Cenar/Dormir
            // cuyo campo Responsable no representaba realmente a una persona.
            // Al mostrarlos en el nuevo programa se asignan al SC y su antiguo
            // valor se conserva como descripción de la actividad.
            return "SC";
        }

        private static int ObtenerOrdenHoraPrograma(string hora)
        {
            if (string.IsNullOrWhiteSpace(hora))
            {
                return int.MaxValue;
            }

            string valor = hora.Trim();

            string[] formatos =
            {
                @"h\:mm",
                @"hh\:mm",
                @"h\.mm",
                @"hh\.mm"
            };

            if (TimeSpan.TryParseExact(
                    valor,
                    formatos,
                    CultureInfo.InvariantCulture,
                    out TimeSpan ts))
            {
                return (int)ts.TotalMinutes;
            }

            if (DateTime.TryParse(
                    valor,
                    CultureInfo.GetCultureInfo("es-ES"),
                    DateTimeStyles.NoCurrentDateDefault,
                    out DateTime dt))
            {
                return dt.Hour * 60 + dt.Minute;
            }

            return int.MaxValue - 1;
        }


        private void CargarNotas(DateTime mesReferencia)
        {
            string mesRefStr = mesReferencia.ToString("yyyy-MM");
            int idVisita = DatabaseHelper.ObtenerIdSemanaVisita(_congreNombre, mesRefStr);

            if (idVisita == 0)
            {
                var nuevaSemana = new SemanaCalendario { FechaMartes = new DateTime(mesReferencia.Year, mesReferencia.Month, 1), Congregacion = _congreNombre, Actividad = "Visita" };
                DatabaseHelper.GuardarSemana(nuevaSemana);
                idVisita = DatabaseHelper.ObtenerIdSemanaVisita(_congreNombre, mesRefStr);
            }

            if (idVisita > 0)
            {
                var notasPlanas = DatabaseHelper.ObtenerNotasVisita(idVisita)
                    .Where(n =>
                    {
                        string pregunta = n.Pregunta?.Trim() ?? "";
                        return !pregunta.Equals(
                                   "¿Tienen los nombres de publicadores la structure correcta?",
                                   StringComparison.OrdinalIgnoreCase)
                               && !pregunta.Equals(
                                   "¿Tienen los nombres de publicadores la estructura correcta?",
                                   StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                MisNotasAgrupadas = notasPlanas.GroupBy(n => n.Cabecera)
                    .Select(g => new CategoriaNotas { Cabecera = g.Key, Preguntas = g.ToList() }).ToList();

                var panelNotas = this.FindName("PanelNotas") as FrameworkElement;
                if (panelNotas != null)
                {
                    // La página no notifica cambios de MisNotasAgrupadas.
                    // Forzamos que el binding se reevalúe también al cambiar de visita/mes.
                    panelNotas.DataContext = null;
                    panelNotas.DataContext = this;
                }
            }
        }

        private void TxtNotaRespuesta_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt && txt.DataContext is NotaVisita nota) DatabaseHelper.ActualizarNotaVisitaUnica(nota);
        }

        private void BtnAlertaNota_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is NotaVisita nota)
            {
                nota.EsAlerta = !nota.EsAlerta;
                DatabaseHelper.ActualizarNotaVisitaUnica(nota);
            }
        }

        private FilaPrograma CrearFilaPrograma(string mesRefStr, string turno, string persona)
        {
            string turnoLimpio = turno.Substring(3);
            return new FilaPrograma
            {
                Turno = turno,
                Persona = persona,
                Miercoles = GenerarCelda(mesRefStr, "Miércoles", turnoLimpio, persona),
                Jueves = GenerarCelda(mesRefStr, "Jueves", turnoLimpio, persona),
                Viernes = GenerarCelda(mesRefStr, "Viernes", turnoLimpio, persona),
                Sabado = GenerarCelda(mesRefStr, "Sábado", turnoLimpio, persona),
                Domingo = GenerarCelda(mesRefStr, "Domingo", turnoLimpio, persona)
            };
        }

        private CeldaPrograma GenerarCelda(string mesRefStr, string dia, string turnoLimpio, string responsable)
        {
            var celda = new CeldaPrograma
            {
                Dia = dia,
                Turno = turnoLimpio,
                Responsable = responsable,
                ListaActividades = new List<BotonActividadUI>()
            };

            var actividadesDB = DatabaseHelper.ObtenerActividadesPrograma(_congreCodigo, mesRefStr, dia, turnoLimpio, responsable);

            foreach (var act in actividadesDB)
            {
                var comp = DatabaseHelper.ObtenerPersonaPorId(act.CompaneroId);
                string nombreComp = comp != null ? comp.Nombre.Split(' ')[0] : "Alguien";

                string texto =
                    responsable == "Comida"
                    || responsable == "Cenar"
                    || responsable == "Dormir"
                        ? $"{act.Hora} - {nombreComp}"
                        : $"{act.Hora} - {act.TipoActividad} ({nombreComp})";

                if (!string.IsNullOrWhiteSpace(
                        act.Direccion))
                {
                    texto +=
                        $"\n📍 {act.Direccion}";
                }

                celda.ListaActividades.Add(
                    new BotonActividadUI
                    {
                        Texto = texto,
                        Dia = dia,
                        Turno = turnoLimpio,
                        Responsable = responsable,
                        ActividadDB = act
                    });
            }

            if ((responsable == "Comida" || responsable == "Cenar" || responsable == "Dormir") && celda.ListaActividades.Count > 0) celda.VerBotonAnadir = Visibility.Collapsed;
            else celda.VerBotonAnadir = Visibility.Visible;

            return celda;
        }

        private void BtnProgramarActividad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn
                &&
                btn.Tag is string dia
                &&
                !string.IsNullOrWhiteSpace(dia))
            {
                var ventana =
                    new ProgramarActividadWindow(
                        "SC",
                        "General",
                        dia,
                        _congreCodigo,
                        _mesPrograma.ToString("yyyy-MM"));

                ventana.Owner =
                    Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    ActualizarUI_MesPrograma();
                }
            }
        }


        private void BtnImportarProgramaJson_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialogo = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importar programa de la visita",
                Filter = "Archivos JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
                Multiselect = false
            };

            if (dialogo.ShowDialog() != true)
            {
                return;
            }

            try
            {
                string contenido = File.ReadAllText(dialogo.FileName);

                using (JsonDocument documento = JsonDocument.Parse(contenido))
                {
                    JsonElement raiz = documento.RootElement;
                    JsonElement listaJson;

                    if (raiz.ValueKind == JsonValueKind.Array)
                    {
                        listaJson = raiz;
                    }
                    else if (raiz.ValueKind == JsonValueKind.Object
                             && TryObtenerPropiedadJson(
                                 raiz,
                                 out JsonElement actividadesElement,
                                 "actividades",
                                 "programa",
                                 "eventos"))
                    {
                        listaJson = actividadesElement;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            "El JSON debe ser un array de actividades o un objeto con una propiedad 'actividades'.");
                    }

                    if (listaJson.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException(
                            "La propiedad de actividades del JSON debe contener una lista.");
                    }

                    var personas =
                        DatabaseHelper
                            .ObtenerPersonasPorCongregacion(_congreCodigo)
                            .Where(p =>
                                p != null
                                && string.IsNullOrWhiteSpace(p.FechaEliminacion))
                            .ToList();

                    var importadas =
                        new List<DatabaseHelper.ActividadPrograma>();

                    var avisos =
                        new List<string>();

                    int numeroRegistro = 0;

                    foreach (JsonElement item in listaJson.EnumerateArray())
                    {
                        numeroRegistro++;

                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            avisos.Add(
                                $"Registro {numeroRegistro}: no es un objeto y se ha omitido.");
                            continue;
                        }

                        string dia =
                            NormalizarDiaProgramaJson(
                                LeerTextoJson(
                                    item,
                                    "dia",
                                    "día",
                                    "diaSemana",
                                    "díaSemana"));

                        string hora =
                            LeerTextoJson(
                                item,
                                "hora");

                        if (string.IsNullOrWhiteSpace(dia))
                        {
                            avisos.Add(
                                $"Registro {numeroRegistro}: día no válido. Solo se admiten miércoles, jueves, viernes, sábado y domingo.");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(hora))
                        {
                            avisos.Add(
                                $"Registro {numeroRegistro} ({dia}): falta la hora y se ha omitido.");
                            continue;
                        }

                        string tipo =
                            NormalizarTipoProgramaJson(
                                LeerTextoJson(
                                    item,
                                    "tipoActividad",
                                    "tipo",
                                    "actividad"));

                        if (string.IsNullOrWhiteSpace(tipo))
                        {
                            tipo = "Otra";
                        }

                        string responsableRaw =
                            LeerTextoJson(
                                item,
                                "responsable");

                        string responsable =
                            NormalizarResponsablePrograma(
                                string.IsNullOrWhiteSpace(responsableRaw)
                                    ? "SC"
                                    : responsableRaw);

                        string nombreAcompanante =
                            LeerTextoJson(
                                item,
                                "acompanante",
                                "acompañante",
                                "companero",
                                "compañero");

                        int companeroId =
                            ResolverAcompananteProgramaJson(
                                personas,
                                nombreAcompanante,
                                out bool acompananteAmbiguo);

                        if (!string.IsNullOrWhiteSpace(nombreAcompanante)
                            &&
                            !EsTextoSinAcompanante(nombreAcompanante)
                            &&
                            companeroId <= 0)
                        {
                            avisos.Add(
                                acompananteAmbiguo
                                    ? $"Registro {numeroRegistro} ({dia} {hora}): hay más de una coincidencia para el acompañante '{nombreAcompanante}'. Se importó sin acompañante."
                                    : $"Registro {numeroRegistro} ({dia} {hora}): no se encontró el acompañante '{nombreAcompanante}'. Se importó sin acompañante.");
                        }

                        importadas.Add(
                            new DatabaseHelper.ActividadPrograma
                            {
                                DiaSemana = dia,
                                Turno = "General",
                                Responsable = responsable,
                                TipoActividad = tipo,
                                Hora = hora.Trim(),
                                NotasLibro =
                                    LeerTextoJson(
                                        item,
                                        "notas",
                                        "nota"),
                                CompaneroId = companeroId,
                                Direccion =
                                    LeerTextoJson(
                                        item,
                                        "direccion",
                                        "dirección",
                                        "ubicacion",
                                        "ubicación"),
                                Latitud =
                                    LeerNumeroJson(
                                        item,
                                        "latitud",
                                        "lat"),
                                Longitud =
                                    LeerNumeroJson(
                                        item,
                                        "longitud",
                                        "lon",
                                        "lng")
                            });
                    }

                    if (importadas.Count == 0)
                    {
                        MessageBox.Show(
                            "No se ha encontrado ninguna actividad válida para importar.",
                            "Importar programa",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    string mesReferencia =
                        _mesPrograma.ToString("yyyy-MM");

                    var existentes =
                        DatabaseHelper.ObtenerActividadesProgramaMes(
                            _congreCodigo,
                            mesReferencia);

                    bool reemplazar = false;

                    if (existentes.Count > 0)
                    {
                        MessageBoxResult decision =
                            MessageBox.Show(
                                "Ya existe actividad en el programa de esta visita.\n\n"
                                + "Sí = reemplazar todo el programa de la semana.\n"
                                + "No = añadir las actividades del JSON al programa actual.\n"
                                + "Cancelar = no importar.",
                                "Importar programa JSON",
                                MessageBoxButton.YesNoCancel,
                                MessageBoxImage.Question);

                        if (decision == MessageBoxResult.Cancel)
                        {
                            return;
                        }

                        reemplazar =
                            decision == MessageBoxResult.Yes;

                        if (!reemplazar)
                        {
                            importadas =
                                importadas
                                    .Where(nueva =>
                                        !existentes.Any(actual =>
                                            SonActividadesProgramaIguales(
                                                actual,
                                                nueva)))
                                    .ToList();

                            if (importadas.Count == 0)
                            {
                                MessageBox.Show(
                                    "Todas las actividades del JSON ya existen en el programa.",
                                    "Importar programa",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                                return;
                            }
                        }
                    }

                    DatabaseHelper.ImportarProgramaVisita(
                        _congreCodigo,
                        mesReferencia,
                        importadas,
                        reemplazar);

                    ActualizarUI_MesPrograma();

                    string resumen =
                        $"Se han importado {importadas.Count} actividades.";

                    if (avisos.Count > 0)
                    {
                        resumen +=
                            "\n\nAvisos:\n• "
                            + string.Join(
                                "\n• ",
                                avisos.Take(8));

                        if (avisos.Count > 8)
                        {
                            resumen +=
                                $"\n• … y {avisos.Count - 8} aviso(s) más.";
                        }
                    }

                    MessageBox.Show(
                        resumen,
                        "Programa importado",
                        MessageBoxButton.OK,
                        avisos.Count == 0
                            ? MessageBoxImage.Information
                            : MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo importar el programa JSON.\n\n"
                    + ex.Message,
                    "Importar programa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static bool TryObtenerPropiedadJson(
            JsonElement elemento,
            out JsonElement valor,
            params string[] nombres)
        {
            if (elemento.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty propiedad in elemento.EnumerateObject())
                {
                    if (nombres.Any(n =>
                        string.Equals(
                            propiedad.Name,
                            n,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        valor = propiedad.Value;
                        return true;
                    }
                }
            }

            valor = default(JsonElement);
            return false;
        }

        private static string LeerTextoJson(
            JsonElement elemento,
            params string[] nombres)
        {
            if (!TryObtenerPropiedadJson(
                    elemento,
                    out JsonElement valor,
                    nombres))
            {
                return "";
            }

            if (valor.ValueKind == JsonValueKind.String)
            {
                return valor.GetString()?.Trim() ?? "";
            }

            if (valor.ValueKind == JsonValueKind.Null
                || valor.ValueKind == JsonValueKind.Undefined)
            {
                return "";
            }

            return valor.ToString().Trim();
        }

        private static double LeerNumeroJson(
            JsonElement elemento,
            params string[] nombres)
        {
            if (!TryObtenerPropiedadJson(
                    elemento,
                    out JsonElement valor,
                    nombres))
            {
                return 0;
            }

            if (valor.ValueKind == JsonValueKind.Number
                && valor.TryGetDouble(out double numero))
            {
                return numero;
            }

            string texto =
                valor.ValueKind == JsonValueKind.String
                    ? valor.GetString()
                    : valor.ToString();

            if (double.TryParse(
                    (texto ?? "").Replace(",", "."),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double resultado))
            {
                return resultado;
            }

            return 0;
        }

        private static string NormalizarDiaProgramaJson(
            string dia)
        {
            string valor =
                (dia ?? "")
                    .Trim()
                    .ToLowerInvariant();

            switch (valor)
            {
                case "miércoles":
                case "miercoles":
                    return "Miércoles";

                case "jueves":
                    return "Jueves";

                case "viernes":
                    return "Viernes";

                case "sábado":
                case "sabado":
                    return "Sábado";

                case "domingo":
                    return "Domingo";

                default:
                    return "";
            }
        }

        private static string NormalizarTipoProgramaJson(
            string tipo)
        {
            string valor =
                (tipo ?? "").Trim();

            if (string.IsNullOrWhiteSpace(valor))
            {
                return "";
            }

            string minusculas =
                valor.ToLowerInvariant();

            if (minusculas == "rsc")
                return "RSC";

            if (minusculas == "predicación"
                || minusculas == "predicacion")
                return "Predicación";

            if (minusculas == "visita de pastoreo"
                || minusculas == "pastoreo")
                return "Visita de pastoreo";

            if (minusculas == "comida")
                return "Comida";

            if (minusculas == "alojamiento")
                return "Alojamiento";

            if (minusculas == "otra"
                || minusculas == "otro")
                return "Otra";

            // Se permiten también descripciones personalizadas.
            return valor;
        }

        private static bool EsTextoSinAcompanante(
            string texto)
        {
            string valor =
                (texto ?? "").Trim();

            return
                string.IsNullOrWhiteSpace(valor)
                || valor.Equals(
                    "sin acompañante",
                    StringComparison.OrdinalIgnoreCase)
                || valor.Equals(
                    "sin acompanante",
                    StringComparison.OrdinalIgnoreCase)
                || valor.Equals(
                    "ninguno",
                    StringComparison.OrdinalIgnoreCase)
                || valor.Equals(
                    "-",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool TextoProgramaCoincide(
            string a,
            string b)
        {
            if (string.IsNullOrWhiteSpace(a)
                || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            return
                CultureInfo
                    .GetCultureInfo("es-ES")
                    .CompareInfo
                    .Compare(
                        a.Trim(),
                        b.Trim(),
                        CompareOptions.IgnoreCase
                        | CompareOptions.IgnoreNonSpace)
                == 0;
        }

        private static int ResolverAcompananteProgramaJson(
            List<Persona> personas,
            string nombre,
            out bool ambiguo)
        {
            ambiguo = false;

            if (EsTextoSinAcompanante(nombre))
            {
                return 0;
            }

            var coincidencias =
                (personas ?? new List<Persona>())
                    .Where(p =>
                        TextoProgramaCoincide(
                            nombre,
                            p.NombreComun)
                        ||
                        TextoProgramaCoincide(
                            nombre,
                            p.NombreJWHub)
                        ||
                        TextoProgramaCoincide(
                            nombre,
                            p.Nombre))
                    .Select(p =>
                        p.PersonaRaizId > 0
                            ? p.PersonaRaizId
                            : p.Id)
                    .Distinct()
                    .ToList();

            if (coincidencias.Count == 1)
            {
                return coincidencias[0];
            }

            ambiguo =
                coincidencias.Count > 1;

            return 0;
        }

        private static bool SonActividadesProgramaIguales(
            DatabaseHelper.ActividadPrograma a,
            DatabaseHelper.ActividadPrograma b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return
                string.Equals(
                    a.DiaSemana?.Trim(),
                    b.DiaSemana?.Trim(),
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    NormalizarResponsablePrograma(a.Responsable),
                    NormalizarResponsablePrograma(b.Responsable),
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    a.TipoActividad?.Trim() ?? "",
                    b.TipoActividad?.Trim() ?? "",
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    a.Hora?.Trim() ?? "",
                    b.Hora?.Trim() ?? "",
                    StringComparison.OrdinalIgnoreCase)
                &&
                a.CompaneroId == b.CompaneroId
                &&
                string.Equals(
                    a.Direccion?.Trim() ?? "",
                    b.Direccion?.Trim() ?? "",
                    StringComparison.OrdinalIgnoreCase);
        }


        private void BtnEliminarActividad_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button btn
                &&
                btn.Tag is BotonActividadUI ui
                &&
                ui.ActividadDB != null)
            {
                string descripcion =
                    string.IsNullOrWhiteSpace(ui.TipoActividad)
                        ? "esta actividad"
                        : $"'{ui.TipoActividad}'";

                if (MessageBox.Show(
                        $"¿Eliminar {descripcion} del {ui.Dia} a las {ui.Hora}?",
                        "Confirmar eliminación",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning)
                    != MessageBoxResult.Yes)
                {
                    return;
                }

                if (ui.ActividadDB.Id > 0)
                {
                    DatabaseHelper.EliminarActividadPrograma(
                        ui.ActividadDB.Id);
                }

                ActualizarUI_MesPrograma();
            }
        }


        private void BtnEditarActividad_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button btn
                &&
                btn.Tag is BotonActividadUI ui
                &&
                ui.ActividadDB != null)
            {
                var ventana =
                    new ProgramarActividadWindow(
                        ui.Responsable,
                        "General",
                        ui.Dia,
                        _congreCodigo,
                        _mesPrograma.ToString("yyyy-MM"),
                        ui.ActividadDB);

                ventana.Owner =
                    Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    ActualizarUI_MesPrograma();
                }
            }
        }


        // ==========================================
        // CREADORES DE MÓDULOS (MINISTERIO Y TERRITORIOS)
        // ==========================================
        private async Task ProcesarColumnasYDatosDinamicos(DateTime[] meses, List<Persona> listadoHermanos)
        {
            while (TablaVisitas.Columns.Count > 1)
            {
                TablaVisitas.Columns.RemoveAt(1);
            }

            var estiloIzquierda = new Style(typeof(TextBlock));
            estiloIzquierda.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            estiloIzquierda.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left));
            estiloIzquierda.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));

            string[] shortMonths = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

            var baseCellStyle = new Style(typeof(DataGridCell));
            baseCellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            baseCellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            baseCellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 0, 0, 0)));

            for (int i = 0; i < 6; i++)
            {
                var estiloCeldaConTooltip = new Style(typeof(DataGridCell), baseCellStyle);
                var bindingNotas = new Binding($"NotasM{i + 1}");

                estiloCeldaConTooltip.Setters.Add(new Setter(ToolTipService.ToolTipProperty, bindingNotas));
                estiloCeldaConTooltip.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 15000));

                var triggerNull = new DataTrigger { Binding = bindingNotas, Value = null };
                triggerNull.Setters.Add(new Setter(ToolTipService.IsEnabledProperty, false));
                var triggerVacio = new DataTrigger { Binding = bindingNotas, Value = "" };
                triggerVacio.Setters.Add(new Setter(ToolTipService.IsEnabledProperty, false));

                estiloCeldaConTooltip.Triggers.Add(triggerNull);
                estiloCeldaConTooltip.Triggers.Add(triggerVacio);

                // Cada celda: horas del mes y, debajo, el acumulado del
                // año de servicio (en gris o rojo si va por debajo).
                string plantillaXaml =
                    "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                        "<StackPanel VerticalAlignment=\"Center\">" +
                            "<TextBlock Text=\"{Binding ActividadM" + (i + 1) + "}\" Foreground=\"{Binding ColorM" + (i + 1) + "}\" VerticalAlignment=\"Center\" />" +
                            "<TextBlock Text=\"{Binding AcumuladoM" + (i + 1) + "}\" Foreground=\"{Binding ColorAcumuladoM" + (i + 1) + "}\" FontSize=\"9.5\" Margin=\"0,-2,0,0\">" +
                                "<TextBlock.Style>" +
                                    "<Style TargetType=\"TextBlock\">" +
                                        "<Setter Property=\"Visibility\" Value=\"Visible\" />" +
                                        "<Style.Triggers>" +
                                            "<DataTrigger Binding=\"{Binding AcumuladoM" + (i + 1) + "}\" Value=\"\">" +
                                                "<Setter Property=\"Visibility\" Value=\"Collapsed\" />" +
                                            "</DataTrigger>" +
                                        "</Style.Triggers>" +
                                    "</Style>" +
                                "</TextBlock.Style>" +
                            "</TextBlock>" +
                        "</StackPanel>" +
                    "</DataTemplate>";

                var plantillaMes =
                    (DataTemplate)System.Windows.Markup.XamlReader.Parse(plantillaXaml);

                TablaVisitas.Columns.Add(new DataGridTemplateColumn
                {
                    Header = $"{shortMonths[meses[i].Month - 1]} {meses[i].ToString("yy")}",
                    CellTemplate = plantillaMes,
                    Width = new DataGridLength(6, DataGridLengthUnitType.Star),
                    CellStyle = estiloCeldaConTooltip
                });
            }

            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "CBs", Binding = new Binding("CursosUltimoMes"), Width = new DataGridLength(5, DataGridLengthUnitType.Star), ElementStyle = estiloIzquierda });
            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "A/SM", Binding = new Binding("Nombramiento"), Width = new DataGridLength(7, DataGridLengthUnitType.Star), ElementStyle = estiloIzquierda });
            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "Prec", Binding = new Binding("Servicio"), Width = new DataGridLength(7, DataGridLengthUnitType.Star), ElementStyle = estiloIzquierda });
            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "Edad", Binding = new Binding("Edad"), Width = new DataGridLength(5, DataGridLengthUnitType.Star), ElementStyle = estiloIzquierda });
            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "Baut.", Binding = new Binding("AniosBautizado"), Width = new DataGridLength(5, DataGridLengthUnitType.Star), ElementStyle = estiloIzquierda });
            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "Sexo", Binding = new Binding("Sexo"), Width = new DataGridLength(6, DataGridLengthUnitType.Star), ElementStyle = estiloIzquierda });
            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "Terrs.", Binding = new Binding("TerritoriosTrabajados"), Width = new DataGridLength(11, DataGridLengthUnitType.Star), ElementStyle = estiloIzquierda });

            var colAcciones = new DataGridTemplateColumn
            {
                Header = "Acción",
                Width = new DataGridLength(9, DataGridLengthUnitType.Star),
                MinWidth = 92,
                CellTemplate = (DataTemplate)this.FindResource("BotonEditarTemplate")
            };
            TablaVisitas.Columns.Add(colAcciones);

            AjustarVisibilidadColumnas(this.ActualWidth);

            var territoriosBD = DatabaseHelper.ObtenerTerritoriosPorCongregacion(_congreCodigo);
            var filasGrid = new List<FilaVisitaPublisher>();

            _idsInactivos.Clear();

            DateTime mesInicioVisualizado = meses[0];
            DateTime mesReferenciaGrid = meses[5];
            DateTime mesFinVisualizado = mesReferenciaGrid.AddMonths(1).AddDays(-1);

            string anioServicioGrid = (mesReferenciaGrid.Month >= 9) ? (mesReferenciaGrid.Year + 1).ToString() : mesReferenciaGrid.Year.ToString();
            int mesesTranscurridos = (mesReferenciaGrid.Month >= 9) ? mesReferenciaGrid.Month - 8 : mesReferenciaGrid.Month + 4;

            string[] nombresMesesJson = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

            int statPersonas = 0, statNoBautizados = 0, statAncianos = 0, statSiervos = 0;
            int statPrecReg = 0, statPrecEsp = 0, statAuxiliares = 0, statBautizados6m = 0;
            int statIrregulares = 0, statCursos = 0;
            int statHombres = 0, statMujeres = 0, statAuxiliaresMesActual = 0;
            List<double> statEdades = new List<double>();

            // Las instantáneas históricas conservan los datos ministeriales del mes,
            // pero los datos de identidad se muestran desde la ficha raíz actual.
            var personasActualesPorId =
                DatabaseHelper
                    .ObtenerPersonasPorCongregacion(_congreCodigo)
                    .ToDictionary(
                        p => p.Id,
                        p => p);

            // Toda la actividad de la congregación en una sola consulta
            // (antes se hacía una por persona: cientos por carga).
            var idsActividad =
                listadoHermanos
                    .Select(h => (int)(h.PersonaRaizId > 0 ? h.PersonaRaizId : h.Id))
                    .Distinct()
                    .ToList();

            var actividadesPorPersona =
                DatabaseHelper.ObtenerActividadesPorPersonas(idsActividad);

            foreach (var h in listadoHermanos)
            {
                DateTime? fElim = ParsearFecha(h.FechaEliminacion);
                if (fElim.HasValue && new DateTime(fElim.Value.Year, fElim.Value.Month, 1) < mesReferenciaGrid) continue;

                DateTime? fCrea = ParsearFecha(h.FechaCreacion);

                // Las personas creadas manualmente en SCCosmos pueden no tener
                // cuenta de JW (NombreJWHub/EmailJW vacíos). En ese caso
                // FechaCreacion es la fecha de alta técnica en la aplicación, no
                // necesariamente la fecha en que empezaron a formar parte de la
                // congregación. No debemos ocultarlas de la página de visita.
                bool personaSinCuentaJW =
                    string.IsNullOrWhiteSpace(h.NombreJWHub)
                    && string.IsNullOrWhiteSpace(h.EmailJW);

                if (!personaSinCuentaJW
                    && fCrea.HasValue
                    && new DateTime(fCrea.Value.Year, fCrea.Value.Month, 1) > mesReferenciaGrid)
                {
                    continue;
                }

                int idRaizActual =
                    h.PersonaRaizId > 0
                        ? h.PersonaRaizId
                        : h.Id;

                personasActualesPorId.TryGetValue(
                    idRaizActual,
                    out Persona personaActual);

                string nombreLimpio =
                    !string.IsNullOrWhiteSpace(personaActual?.Nombre)
                        ? personaActual.Nombre.Trim()
                        : (!string.IsNullOrWhiteSpace(h.Nombre) ? h.Nombre.Trim() : "");

                string nombreComunLimpio =
                    !string.IsNullOrWhiteSpace(personaActual?.NombreComun)
                        ? personaActual.NombreComun.Trim()
                        : (!string.IsNullOrWhiteSpace(h.NombreComun)
                            ? h.NombreComun.Trim()
                            : nombreLimpio);

                string jwHubLimpio =
                    !string.IsNullOrWhiteSpace(personaActual?.NombreJWHub)
                        ? personaActual.NombreJWHub.Trim()
                        : (!string.IsNullOrWhiteSpace(h.NombreJWHub)
                            ? h.NombreJWHub.Trim()
                            : "");

                string nombramientoLimpio = !string.IsNullOrWhiteSpace(h.Nombramiento) ? h.Nombramiento.Trim() : "";
                string servicioLimpio = !string.IsNullOrWhiteSpace(h.Servicio) ? h.Servicio.Trim() : "";
                string sexoLimpio = !string.IsNullOrWhiteSpace(h.Sexo) ? h.Sexo.Trim() : "-";

                bool esPrec = !string.IsNullOrWhiteSpace(servicioLimpio) && (servicioLimpio.ToLower().Contains("precursor") || servicioLimpio.ToLower().Contains("misionero"));

                long idParaActividad = h.PersonaRaizId > 0 ? h.PersonaRaizId : h.Id;
                var informesHistoricos =
                    actividadesPorPersona.TryGetValue((int)idParaActividad, out var informesPersona)
                        ? informesPersona
                        : new List<ActividadMensual>();
                int conteoDevueltos = 0;

                foreach (var t in territoriosBD)
                {
                    if (string.Equals(t.NombreVinculado?.Trim(), nombreLimpio, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(t.FechaCompletado))
                    {
                        DateTime? fechaComp = ParsearFecha(t.FechaCompletado);
                        if (fechaComp.HasValue && fechaComp.Value >= mesInicioVisualizado && fechaComp.Value <= mesFinVisualizado) conteoDevueltos++;
                    }
                }

                DateTime? fn = ParsearFecha(h.FechaNacimiento);
                DateTime? fb = ParsearFecha(h.FechaBautismo);
                double edadNumerica = fn.HasValue ? Math.Round((DateTime.Today - fn.Value).TotalDays / 365.2425, 1) : 0;
                double bautNumerica = fb.HasValue ? Math.Round((DateTime.Today - fb.Value).TotalDays / 365.2425, 1) : 0;

                // ── PUBLICADOR INACTIVO ────────────────────────────
                // Se considera inactivo quien lleva 6 meses seguidos sin
                // informar actividad (ni horas ni participación). No se
                // aplica a fichas creadas dentro de esos 6 meses.
                bool reportoActividad = false;

                for (int i = 0; i < 6; i++)
                {
                    string ms = nombresMesesJson[meses[i].Month - 1];
                    string ys = (meses[i].Month >= 9)
                        ? (meses[i].Year + 1).ToString()
                        : meses[i].Year.ToString();

                    var reg = informesHistoricos.FirstOrDefault(
                        x => x.Mes.ToLower() == ms && x.AnioServicio == ys);

                    if (reg == null)
                    {
                        continue;
                    }

                    string horasRaw = (reg.Horas ?? "").Replace(" ", "").Trim();
                    bool tieneHoras = !string.IsNullOrEmpty(horasRaw)
                        && horasRaw != "0" && horasRaw != "0.0" && horasRaw != "0,0";

                    if (tieneHoras || reg.Participacion)
                    {
                        reportoActividad = true;
                        break;
                    }
                }

                bool fichaReciente = fCrea.HasValue
                    && new DateTime(fCrea.Value.Year, fCrea.Value.Month, 1) > mesInicioVisualizado;

                // Sin ningún registro histórico no se puede saber si está
                // inactivo (p. ej. fichas que solo existen en JW Hub): se
                // deja como activo y solo se marca inactivo a quien tenía
                // actividad y lleva 6 meses sin informar nada.
                bool tieneHistorial = informesHistoricos.Count > 0;

                bool esInactivo = tieneHistorial && !reportoActividad && !fichaReciente;

                if (esInactivo)
                {
                    _idsInactivos.Add(idRaizActual);
                }

                // Los inactivos no cuentan en las estadísticas ni en el
                // número de publicadores activos.
                if (h.Grupo > 0 && !esInactivo)
                {
                    statPersonas++;
                    if (sexoLimpio.Equals("Hombre", StringComparison.OrdinalIgnoreCase)) statHombres++;
                    if (sexoLimpio.Equals("Mujer", StringComparison.OrdinalIgnoreCase)) statMujeres++;
                    if (!fb.HasValue) statNoBautizados++;
                    else if (fb.Value >= mesInicioVisualizado && fb.Value <= mesFinVisualizado) statBautizados6m++;

                    if (fn.HasValue && fn.Value.Year > 1900 && edadNumerica > 0 && edadNumerica <= 120) statEdades.Add(edadNumerica);

                    string nomb = nombramientoLimpio.ToLower();
                    if (nomb.Contains("anciano")) statAncianos++;
                    if (nomb.Contains("siervo") || nomb.Contains("ministerial")) statSiervos++;

                    string serv = servicioLimpio.ToLower();
                    if (serv.Contains("precursor regular")) statPrecReg++;
                    if (serv.Contains("especial") || serv.Contains("misionero")) statPrecEsp++;

                    bool esIrregular = false;
                    bool hizoAux = false;

                    for (int i = 0; i < 6; i++)
                    {
                        string ms = nombresMesesJson[meses[i].Month - 1];
                        string ys = (meses[i].Month >= 9) ? (meses[i].Year + 1).ToString() : meses[i].Year.ToString();
                        var regInfo = informesHistoricos.FirstOrDefault(x => x.Mes.ToLower() == ms && x.AnioServicio == ys);

                        if (regInfo == null) esIrregular = true;
                        else
                        {
                            string rawHoras = regInfo.Horas?.Replace(" ", "").Trim();
                            bool tieneHoras = !string.IsNullOrEmpty(rawHoras) && rawHoras != "0" && rawHoras != "0.0" && rawHoras != "0,0";
                            if (!tieneHoras && !regInfo.Participacion) esIrregular = true;
                            if (regInfo.PrecursorAuxiliar) hizoAux = true;
                        }
                    }
                    if (esIrregular) statIrregulares++;
                    if (hizoAux) statAuxiliares++;

                    string msUlt = nombresMesesJson[mesReferenciaGrid.Month - 1];
                    string ysUlt = (mesReferenciaGrid.Month >= 9) ? (mesReferenciaGrid.Year + 1).ToString() : mesReferenciaGrid.Year.ToString();
                    var regUlt = informesHistoricos.FirstOrDefault(x => x.Mes.ToLower() == msUlt && x.AnioServicio == ysUlt);
                    if (regUlt != null)
                    {
                        if (int.TryParse(regUlt.CursosBiblicos?.Replace(" ", "").Trim(), out int cbMesUlt)) statCursos += cbMesUlt;
                        if (regUlt.PrecursorAuxiliar) statAuxiliaresMesActual++;
                    }
                }

                int requisitoAnual = h.RequisitoHoras;
                string srv = servicioLimpio.ToLower();
                bool esPrecursorRegular = srv.Contains("precursor regular");

                DateTime inicioAnioServicioGrid =
                    new DateTime(int.Parse(anioServicioGrid) - 1, 9, 1);

                DateTime finAnioServicioGrid =
                    new DateTime(int.Parse(anioServicioGrid), 8, 31);

                DateTime inicioMetaPrecursor = inicioAnioServicioGrid;

                // Para los precursores regulares la meta es de 50 h por cada mes
                // en el que ya tenían el precursorado dentro del año de servicio.
                // Ej.: inicio 01/06/2026 => junio, julio y agosto = 150 h.
                if (esPrecursorRegular)
                {
                    DateTime? fechaInicioPrecursor = ParsearFecha(h.FechaInicioPrec);

                    if (fechaInicioPrecursor.HasValue)
                    {
                        DateTime primerMesPrecursor =
                            new DateTime(
                                fechaInicioPrecursor.Value.Year,
                                fechaInicioPrecursor.Value.Month,
                                1);

                        if (primerMesPrecursor > inicioMetaPrecursor)
                            inicioMetaPrecursor = primerMesPrecursor;
                    }

                    if (inicioMetaPrecursor > finAnioServicioGrid)
                    {
                        requisitoAnual = 0;
                    }
                    else
                    {
                        int mesesComoPrecursor =
                            ((finAnioServicioGrid.Year - inicioMetaPrecursor.Year) * 12)
                            + finAnioServicioGrid.Month
                            - inicioMetaPrecursor.Month
                            + 1;

                        requisitoAnual = Math.Max(0, mesesComoPrecursor) * 50;
                    }

                    if (h.RequisitoHoras != requisitoAnual)
                    {
                        h.RequisitoHoras = requisitoAnual;
                        DatabaseHelper.ActualizarRequisitoSilencioso((int)idParaActividad, requisitoAnual);
                    }
                }
                else if (requisitoAnual == 0 && esPrec)
                {
                    if (srv.Contains("especial") || srv.Contains("misionero"))
                    {
                        requisitoAnual = 1200;
                        if (sexoLimpio.ToLower() == "mujer" && fn.HasValue)
                        {
                            int edadPrec = mesReferenciaGrid.Year - fn.Value.Year;
                            if (fn.Value.Date > mesReferenciaGrid.AddYears(-edadPrec)) edadPrec--;
                            if (edadPrec >= 40) requisitoAnual = 1080;
                        }
                    }

                    if (requisitoAnual > 0)
                    {
                        h.RequisitoHoras = requisitoAnual;
                        DatabaseHelper.ActualizarRequisitoSilencioso((int)idParaActividad, requisitoAnual);
                    }
                }

                // Todos los nombres parten del mismo margen/alineación.
                // La condición de precursor se representa con negrita en XAML,
                // no añadiendo espacios al texto.
                string nombreMostrar = nombreComunLimpio;

                string tooltipAlerta = null;

                if (requisitoAnual > 0)
                {
                    double metaProrrateada;

                    if (esPrecursorRegular)
                    {
                        DateTime mesCorteMeta =
                            new DateTime(
                                mesReferenciaGrid.Year,
                                mesReferenciaGrid.Month,
                                1);

                        DateTime ultimoMesServicio =
                            new DateTime(
                                finAnioServicioGrid.Year,
                                finAnioServicioGrid.Month,
                                1);

                        if (mesCorteMeta > ultimoMesServicio)
                            mesCorteMeta = ultimoMesServicio;

                        if (mesCorteMeta < inicioMetaPrecursor)
                        {
                            metaProrrateada = 0;
                        }
                        else
                        {
                            int mesesMetaHastaCorte =
                                ((mesCorteMeta.Year - inicioMetaPrecursor.Year) * 12)
                                + mesCorteMeta.Month
                                - inicioMetaPrecursor.Month
                                + 1;

                            metaProrrateada = Math.Max(0, mesesMetaHastaCorte) * 50.0;
                        }
                    }
                    else
                    {
                        metaProrrateada =
                            (requisitoAnual / 12.0) * mesesTranscurridos;
                    }

                    double horasTotales = 0;

                    foreach (var act in informesHistoricos.Where(a => a.AnioServicio == anioServicioGrid))
                    {
                        int numMesAct = Array.IndexOf(nombresMesesJson, act.Mes.ToLower()) + 1;
                        int ordenMesAct = (numMesAct >= 9) ? numMesAct - 8 : numMesAct + 4;

                        bool mesDentroMeta = ordenMesAct <= mesesTranscurridos;

                        if (esPrecursorRegular && numMesAct > 0)
                        {
                            int anioMesActividad =
                                numMesAct >= 9
                                    ? int.Parse(anioServicioGrid) - 1
                                    : int.Parse(anioServicioGrid);

                            DateTime fechaMesActividad =
                                new DateTime(anioMesActividad, numMesAct, 1);

                            mesDentroMeta =
                                mesDentroMeta
                                && fechaMesActividad >= inicioMetaPrecursor;
                        }

                        if (mesDentroMeta)
                        {
                            string hrClean = act.Horas?.Replace(" ", "").Trim();
                            if (double.TryParse(
                                    hrClean?.Replace(",", "."),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out double hMes))
                            {
                                horasTotales += hMes;
                            }
                        }
                    }

                    // Ya no se marca el nombre con ⚠️: el atraso se ve
                    // en el acumulado del año en rojo, bajo cada mes.
                }

                var act1 = LocalizarActividadMes(informesHistoricos, meses[0]);
                var act2 = LocalizarActividadMes(informesHistoricos, meses[1]);
                var act3 = LocalizarActividadMes(informesHistoricos, meses[2]);
                var act4 = LocalizarActividadMes(informesHistoricos, meses[3]);
                var act5 = LocalizarActividadMes(informesHistoricos, meses[4]);
                var act6 = LocalizarActividadMes(informesHistoricos, meses[5]);

                // Acumulado del año de servicio hasta cada mes (solo
                // precursores con requisito anual). Los regulares suman
                // 50 h/mes; especiales y misioneros, su cuota mensual.
                double metaMensual =
                    esPrecursorRegular
                        ? 50
                        : (srv.Contains("especial") || srv.Contains("misionero"))
                            ? requisitoAnual / 12.0
                            : 0;

                DateTime? inicioPrecursor =
                    ParsearFecha(h.FechaInicioPrec);

                var acum1 = CrearAcumuladoMes(informesHistoricos, meses[0], metaMensual, esPrecursorRegular, inicioPrecursor);
                var acum2 = CrearAcumuladoMes(informesHistoricos, meses[1], metaMensual, esPrecursorRegular, inicioPrecursor);
                var acum3 = CrearAcumuladoMes(informesHistoricos, meses[2], metaMensual, esPrecursorRegular, inicioPrecursor);
                var acum4 = CrearAcumuladoMes(informesHistoricos, meses[3], metaMensual, esPrecursorRegular, inicioPrecursor);
                var acum5 = CrearAcumuladoMes(informesHistoricos, meses[4], metaMensual, esPrecursorRegular, inicioPrecursor);
                var acum6 = CrearAcumuladoMes(informesHistoricos, meses[5], metaMensual, esPrecursorRegular, inicioPrecursor);

                var nuevaFila = new FilaVisitaPublisher
                {
                    PersonaId = h.Id,
                    PersonaRaizId = h.PersonaRaizId,
                    Nombre = nombreLimpio,
                    NombreComun = nombreComunLimpio,
                    NombreVisual = nombreMostrar,
                    AlertaTooltip = tooltipAlerta,
                    NombreJWHub = jwHubLimpio,
                    Grupo = h.Grupo,
                    Nombramiento = nombramientoLimpio,
                    Servicio = servicioLimpio,
                    EsPrecursor = esPrec,
                    EsInactivo = esInactivo,
                    Edad = (fn.HasValue && fn.Value.Year > 1900 && edadNumerica > 0 && edadNumerica <= 120) ? edadNumerica.ToString("0.0") : "-",
                    AniosBautizado = (fb.HasValue && fb.Value.Year > 1900 && bautNumerica > 0 && bautNumerica <= 120) ? bautNumerica.ToString("0.0") : "-",
                    Sexo = sexoLimpio,
                    FechaNacimientoReal = fn ?? DateTime.MaxValue,
                    Foto = personaActual?.Foto ?? h.Foto,

                    ActividadM1 = act1.Valor,
                    NotasM1 = act1.Notas,
                    ColorM1 = act1.ColorValor,
                    ActividadM2 = act2.Valor,
                    NotasM2 = act2.Notas,
                    ColorM2 = act2.ColorValor,
                    ActividadM3 = act3.Valor,
                    NotasM3 = act3.Notas,
                    ColorM3 = act3.ColorValor,
                    ActividadM4 = act4.Valor,
                    NotasM4 = act4.Notas,
                    ColorM4 = act4.ColorValor,
                    ActividadM5 = act5.Valor,
                    NotasM5 = act5.Notas,
                    ColorM5 = act5.ColorValor,
                    ActividadM6 = act6.Valor,
                    NotasM6 = act6.Notas,
                    ColorM6 = act6.ColorValor,

                    AcumuladoM1 = acum1.Texto,
                    ColorAcumuladoM1 = acum1.Color,
                    AcumuladoM2 = acum2.Texto,
                    ColorAcumuladoM2 = acum2.Color,
                    AcumuladoM3 = acum3.Texto,
                    ColorAcumuladoM3 = acum3.Color,
                    AcumuladoM4 = acum4.Texto,
                    ColorAcumuladoM4 = acum4.Color,
                    AcumuladoM5 = acum5.Texto,
                    ColorAcumuladoM5 = acum5.Color,
                    AcumuladoM6 = acum6.Texto,
                    ColorAcumuladoM6 = acum6.Color,

                    CursosUltimoMes = string.IsNullOrWhiteSpace(act6.Cursos) || act6.Cursos.Trim() == "0" ? "-" : act6.Cursos.Trim(),
                    TerritoriosTrabajados = conteoDevueltos.ToString()
                };
                filasGrid.Add(nuevaFila);
            }

            _totalPublicadoresActivos = statPersonas;

            var estadisticasUI = new EstadisticasInformacion
            {
                TotalPersonas = statPersonas,
                TotalHombres = statHombres,
                TotalMujeres = statMujeres,
                NoBautizados = statNoBautizados,
                Ancianos = statAncianos,
                SiervosMinisteriales = statSiervos,
                PrecursoresRegulares = statPrecReg,
                PrecursoresEspeciales = statPrecEsp,
                AuxiliaresMesEscogido = statAuxiliaresMesActual,
                Auxiliares6Meses = statAuxiliares,
                BautizadosRecientes = statBautizados6m,
                Irregulares = statIrregulares,
                MediaEdad = statEdades.Any() ? statEdades.Average().ToString("F1") : "0.0",
                TotalCursos = statCursos,
                MediaCursos = statPersonas > 0 ? ((double)statCursos / statPersonas).ToString("0.00") : "0.00"
            };

            var panelVista = this.FindName("PanelEstadisticas") as FrameworkElement;
            if (panelVista != null) panelVista.DataContext = estadisticasUI;

            _filasVisitasGlobal = filasGrid.OrderBy(f => (f.NombreComun ?? "").Replace("(", "").Replace(")", "").Trim()).ThenBy(f => f.FechaNacimientoReal).ToList();

            // Cada grupo muestra cuántas personas ACTIVAS tiene (también
            // «SIN ASIGNAR»). Los inactivos van en un bloque aparte al
            // final y no suman en ningún grupo.
            var activos = _filasVisitasGlobal
                .Where(f => !f.EsInactivo)
                .ToList();

            var gruposUnicos = activos
                .GroupBy(f => f.GrupoPredicacion)
                .Select(g => new GrupoVisitaUI
                {
                    Nombre = g.Key,
                    Cantidad = g.Count()
                })
                .OrderBy(g => g.Nombre == "SIN ASIGNAR" ? 1 : 0)
                .ThenBy(g => g.Nombre)
                .ToList();

            // Los inactivos van dentro de su grupo (y de «SIN ASIGNAR»),
            // al final de la tabla y bajo su propio apartado.
            TabGrupos.SelectionChanged -= TabGrupos_SelectionChanged;
            TabGrupos.ItemsSource = gruposUnicos;

            // El índice se fija con el manejador desconectado y se aplica el
            // filtro UNA sola vez (antes se hacía dos: una por el cambio de
            // índice y otra a mano, duplicando el trabajo de la tabla).
            if (gruposUnicos.Count > 0)
            {
                int idx = TabGrupos.SelectedIndex >= 0 ? TabGrupos.SelectedIndex : 0;
                if (idx >= gruposUnicos.Count) idx = 0;

                TabGrupos.SelectedIndex = idx;
            }

            TabGrupos.SelectionChanged += TabGrupos_SelectionChanged;

            if (gruposUnicos.Count > 0)
            {
                AplicarFiltrosMinisterio();
            }
            else
            {
                TablaVisitas.ItemsSource = null;
            }
        }

        private void CargarGruposPredicacion(List<Persona> listadoHermanos)
        {
            var estadisticas = listadoHermanos
                .Where(p => p.Grupo > 0)
                // Los publicadores inactivos no cuentan en las
                // estadísticas de los grupos.
                .Where(p => !_idsInactivos.Contains(p.PersonaRaizId > 0 ? p.PersonaRaizId : p.Id))
                .GroupBy(p => p.Grupo)
                .Select(g => new EstadisticasGrupo
                {
                    NumeroGrupo = g.Key,
                    NumPublicadores = g.Count(),
                    NumPrecursores = g.Count(p => !string.IsNullOrWhiteSpace(p.Servicio) && (p.Servicio.ToLower().Contains("precursor") || p.Servicio.ToLower().Contains("misionero"))),
                    NumAncianos = g.Count(p => !string.IsNullOrWhiteSpace(p.Nombramiento) && p.Nombramiento.ToLower().Contains("anciano")),
                    NumSM = g.Count(p => !string.IsNullOrWhiteSpace(p.Nombramiento) && (p.Nombramiento.ToLower().Contains("siervo") || p.Nombramiento.ToLower().Contains("ministerial")))
                })
                .OrderBy(g => g.NumeroGrupo)
                .ToList();

            if (GraficoGrupos != null) GraficoGrupos.ItemsSource = estadisticas;
        }

        private void TabGrupos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            AplicarFiltrosMinisterio();
        }

        private void TxtBuscarMinisterio_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Igual que en la página Personas: se espera a que el usuario
            // deje de escribir antes de filtrar (escribir letra a letra
            // bloqueaba la página).
            if (_timerBusquedaMinisterio == null) return;

            _timerBusquedaMinisterio.Stop();
            _timerBusquedaMinisterio.Start();
        }

        private void TimerBusquedaMinisterio_Tick(object sender, EventArgs e)
        {
            _timerBusquedaMinisterio.Stop();

            _busquedaMinisterio = TxtBuscarMinisterio?.Text?.Trim() ?? "";
            AplicarFiltrosMinisterio();
        }

        private void AplicarFiltrosMinisterio()
        {
            if (TablaVisitas == null) return;

            IEnumerable<FilaVisitaPublisher> consulta = _filasVisitasGlobal;

            string busqueda = NormalizarTextoBusquedaMinisterio(_busquedaMinisterio);

            // Si hay búsqueda, se mira en TODOS los grupos (no solo en el
            // que esté seleccionado).
            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                consulta = consulta.Where(f =>
                    ContieneBusquedaMinisterio(f.NombreComun, busqueda) ||
                    ContieneBusquedaMinisterio(f.Nombre, busqueda) ||
                    ContieneBusquedaMinisterio(f.NombreJWHub, busqueda));
            }
            else if (TabGrupos?.SelectedItem is GrupoVisitaUI grupoSeleccionado)
            {
                // Los inactivos del grupo se muestran también, pero en un
                // apartado aparte al final de la tabla.
                consulta = consulta.Where(f =>
                    string.Equals(
                        f.GrupoPredicacion,
                        grupoSeleccionado.Nombre,
                        StringComparison.OrdinalIgnoreCase));
            }

            // Activos primero y, debajo, los inactivos bajo su cabecera.
            var lista = consulta
                .OrderBy(f => f.EsInactivo)
                .ThenBy(f => f.NombreComun ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(f => f.FechaNacimientoReal)
                .ToList();

            var vista = System.Windows.Data.CollectionViewSource
                .GetDefaultView(lista);

            vista.GroupDescriptions.Clear();

            if (lista.Any(f => f.EsInactivo)
                && string.IsNullOrWhiteSpace(busqueda))
            {
                vista.GroupDescriptions.Add(
                    new System.Windows.Data.PropertyGroupDescription("SeccionTabla"));
            }

            TablaVisitas.ItemsSource = vista;

            // Al cambiar las columnas y las filas hay que recalcular los
            // anchos (con estrellas) una vez la tabla ya está maquetada; si
            // no, las últimas columnas se salen por la derecha. Se hace una
            // sola vez (antes se lanzaba dos veces por el doble filtro).
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (TablaVisitas == null) return;

                TablaVisitas.UpdateLayout();

                foreach (var col in TablaVisitas.Columns)
                {
                    if (col.Width.UnitType != DataGridLengthUnitType.Star)
                    {
                        continue;
                    }

                    var anchoActual = col.Width;
                    col.Width = new DataGridLength(0);
                    col.Width = anchoActual;
                }
            }), DispatcherPriority.Loaded);
        }

        private static bool ContieneBusquedaMinisterio(string texto, string busquedaNormalizada)
        {
            if (string.IsNullOrWhiteSpace(texto) || string.IsNullOrWhiteSpace(busquedaNormalizada)) return false;
            return NormalizarTextoBusquedaMinisterio(texto).Contains(busquedaNormalizada);
        }

        private static string NormalizarTextoBusquedaMinisterio(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";

            string normalizado = texto.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (char c in normalizado)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToUpperInvariant(c));
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private bool PersonaVisibleEnMesMinisterio(
            Persona persona,
            DateTime mesReferencia)
        {
            if (persona == null) return false;

            DateTime mesRef =
                new DateTime(
                    mesReferencia.Year,
                    mesReferencia.Month,
                    1);

            DateTime? fElim =
                ParsearFecha(
                    persona.FechaEliminacion);

            if (fElim.HasValue
                && new DateTime(
                    fElim.Value.Year,
                    fElim.Value.Month,
                    1) < mesRef)
            {
                return false;
            }

            DateTime? fCrea =
                ParsearFecha(
                    persona.FechaCreacion);

            bool personaSinCuentaJW =
                string.IsNullOrWhiteSpace(
                    persona.NombreJWHub)
                &&
                string.IsNullOrWhiteSpace(
                    persona.EmailJW);

            if (!personaSinCuentaJW
                && fCrea.HasValue
                && new DateTime(
                    fCrea.Value.Year,
                    fCrea.Value.Month,
                    1) > mesRef)
            {
                return false;
            }

            return true;
        }

        private async Task CargarTerritorios(
            int anioServicio,
            DateTime inicioAnioServicio,
            DateTime finAnioServicio,
            List<Persona> personasMes)
        {
            var listaPlana =
                DatabaseHelper
                    .ObtenerTerritoriosPorCongregacion(
                        _congreCodigo);

            // Los territorios se muestran por orden de alta en la BBDD.
            // Como cada territorio puede tener varias asignaciones, usamos el
            // menor Id de sus registros como posición de creación del territorio.
            var grupos =
                listaPlana
                    .GroupBy(
                        t =>
                            t.Nombre)
                    .OrderBy(
                        g =>
                            g.Min(t => t.Id))
                    .ToList();

            // Mismo conjunto histórico que Ministerio, pero nombres actuales de la raíz.
            var personasActualesTerritorio =
                DatabaseHelper
                    .ObtenerPersonasPorCongregacion(_congreCodigo)
                    .ToDictionary(
                        p => p.Id,
                        p => p);

            DateTime mesReferenciaPersonas =
                new DateTime(
                    _mesMinisterio.Year,
                    _mesMinisterio.Month,
                    1);

            var personasS21Disponibles =
                personasMes
                    .Where(
                        p =>
                            PersonaVisibleEnMesMinisterio(
                                p,
                                mesReferenciaPersonas))
                    .Where(
                        p =>
                            p.Grupo > 0
                            ||
                            string.IsNullOrWhiteSpace(
                                p.NombreJWHub)
                            ||
                            p.Nombre !=
                                p.NombreJWHub)
                    .Select(
                        p =>
                        {
                            int idRaiz =
                                p.PersonaRaizId > 0
                                    ? p.PersonaRaizId
                                    : p.Id;

                            personasActualesTerritorio.TryGetValue(
                                idRaiz,
                                out Persona actual);

                            string nombreActual =
                                !string.IsNullOrWhiteSpace(actual?.Nombre)
                                    ? actual.Nombre.Trim()
                                    : (p.Nombre?.Trim() ?? "");

                            string nombreComunActual =
                                !string.IsNullOrWhiteSpace(actual?.NombreComun)
                                    ? actual.NombreComun.Trim()
                                    : (p.NombreComun?.Trim() ?? "");

                            string nombreJwActual =
                                !string.IsNullOrWhiteSpace(actual?.NombreJWHub)
                                    ? actual.NombreJWHub.Trim()
                                    : (p.NombreJWHub?.Trim() ?? "");

                            return new OpcionPersonaTerritorio
                            {
                                PersonaId = idRaiz,
                                NombreS21 = nombreActual,
                                NombreComun = nombreComunActual,
                                NombreJWHub = nombreJwActual
                            };
                        })
                    .Where(
                        p =>
                            !string.IsNullOrWhiteSpace(
                                p.NombreS21))
                    .GroupBy(
                        p =>
                            p.PersonaId > 0
                                ? $"ID:{p.PersonaId}"
                                : $"NOMBRE:{p.NombreS21}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(
                        g => g.First())
                    .OrderBy(
                        p => p.NombreVisible,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(
                        p =>
                            p.NombreS21,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            personasS21Disponibles.Insert(
                0,
                new OpcionPersonaTerritorio
                {
                    NombreS21 = "-- Sin Asignar --",
                    NombreComun = ""
                });

            var filasGrid =
                new List<FilaTablaTerritorio>();


            // Para un año pasado, las estadísticas se calculan
            // al 31 de agosto. Para el año de servicio actual,
            // se calculan hasta hoy.
            DateTime fechaCorte;

            if (DateTime.Today <
                inicioAnioServicio)
            {
                fechaCorte =
                    inicioAnioServicio;
            }
            else if (DateTime.Today >
                     finAnioServicio)
            {
                fechaCorte =
                    finAnioServicio;
            }
            else
            {
                fechaCorte =
                    DateTime.Today;
            }


            // "Últimos 6 meses" es una ventana móvil real respecto a la fecha
            // de corte. Puede atravesar el 1 de septiembre y, por tanto, incluir
            // devoluciones del año de servicio anterior si siguen estando dentro
            // de esos seis meses. Esta estadística es independiente de la tarjeta
            // "trabajados en el año de servicio", que sí se limita a septiembre-agosto.
            DateTime inicioUltimos6Meses =
                fechaCorte.AddMonths(-6).AddDays(1);

            // Mostramos de forma explícita la fecha a la que se refiere
            // "Trabajándose en...".
            if (TxtEtiquetaTerritoriosEnCurso != null)
            {
                string mesCorte = fechaCorte.ToString("MMM yyyy", new CultureInfo("es-ES"));
                if (!string.IsNullOrEmpty(mesCorte))
                {
                    mesCorte = char.ToUpper(mesCorte[0]) + mesCorte.Substring(1);
                }
                TxtEtiquetaTerritoriosEnCurso.Text = $"Trabajándose en {mesCorte}";
            }


            DateTime fecha4MesesAntes =
                fechaCorte.AddMonths(-4);


            int totalTerritorios =
                0;

            int trabajados6Meses =
                0;

            int trabajadosAnioServicio =
                0;

            int trabajandoseAhora =
                0;

            int caducados =
                0;

            int sinTrabajar1Ano =
                0;

            int sinTrabajar2Anos =
                0;


            var cacheFuzzy =
                new Dictionary<string, string>();

            foreach (var grupo
                     in grupos)
            {
                var asignaciones =
                    grupo
                        .ToList();


                DateTime? fechaCreacion =
                    asignaciones
                        .Select(
                            a =>
                                ParsearFecha(
                                    a.FechaCreacion))
                        .Where(
                            f =>
                                f.HasValue)
                        .Select(
                            f =>
                                f.Value)
                        .DefaultIfEmpty(
                            DateTime.MinValue)
                        .Min();

                if (fechaCreacion ==
                    DateTime.MinValue)
                {
                    fechaCreacion =
                        null;
                }


                DateTime? fechaEliminacion =
                    asignaciones
                        .Select(
                            a =>
                                ParsearFecha(
                                    a.FechaEliminacion))
                        .Where(
                            f =>
                                f.HasValue)
                        .Select(
                            f =>
                                f.Value)
                        .DefaultIfEmpty(
                            DateTime.MaxValue)
                        .Min();

                if (fechaEliminacion ==
                    DateTime.MaxValue)
                {
                    fechaEliminacion =
                        null;
                }


                // Primero obtenemos los registros que realmente pertenecen
                // al año de servicio seleccionado. El año de servicio 2026,
                // por ejemplo, abarca del 01/09/2025 al 31/08/2026.
                var asignacionesAnio =
                    asignaciones
                        .Where(
                            a =>
                                AsignacionIntersecaRango(
                                    a,
                                    inicioAnioServicio,
                                    finAnioServicio))
                        .OrderBy(
                            a =>
                                ParsearFecha(
                                    a.FechaAsignado)
                                ??
                                ParsearFecha(
                                    a.FechaCompletado)
                                ??
                                DateTime.MinValue)
                        .ToList();


                // Un territorio debe aparecer y computar aunque no tenga
                // ninguna asignación dentro del año de servicio seleccionado.
                // FechaCreacion puede ser solo la fecha de importación a SCCosmos,
                // por lo que nunca la usamos para ocultar años históricos.
                // Sí respetamos una eliminación explícita anterior al periodo.
                if (fechaEliminacion.HasValue
                    && fechaEliminacion.Value.Date < inicioAnioServicio.Date)
                {
                    continue;
                }


                totalTerritorios++;


                DateTime? ultimaVez =
                    asignaciones
                        .Select(
                            a =>
                                ParsearFecha(
                                    a.FechaCompletado))
                        .Where(
                            f =>
                                f.HasValue
                                &&
                                f.Value.Date <=
                                fechaCorte.Date)
                        .Select(
                            f =>
                                (DateTime?)f.Value)
                        .OrderByDescending(
                            f =>
                                f)
                        .FirstOrDefault();


                var fila =
                    new FilaTablaTerritorio
                    {
                        Nombre =
                            grupo.Key,

                        UltimaVez =
                            ultimaVez.HasValue
                                ? ultimaVez.Value
                                    .ToString(
                                        "dd/MM/yyyy")
                                : (
                                    asignaciones
                                        .FirstOrDefault()
                                        ?.UltimaFechaCompletado
                                    ??
                                    ""
                                  ),

                        RegistrosAnioServicio =
                            asignacionesAnio
                                .Select(
                                    a =>
                                        CrearCeldaVuelta(
                                            a,
                                            personasS21Disponibles,
                                            cacheFuzzy))
                                .ToList()
                    };


                filasGrid.Add(
                    fila);


                bool trabajadoEn6Meses =
                    false;

                bool trabajadoEnAnio =
                    false;

                bool enCurso =
                    false;

                bool caducado =
                    false;

                // UltimaFechaCompletado del JSON puede contener una devolución
                // anterior que no exista como registro independiente en la lista
                // importada. La incorporamos para que las estadísticas móviles
                // puedan mirar correctamente al año de servicio anterior.
                DateTime? ultimaDevolucionValida =
                    asignaciones
                        .Select(a => ParsearFecha(a.UltimaFechaCompletado))
                        .Where(f => f.HasValue && f.Value.Date <= fechaCorte.Date)
                        .Select(f => (DateTime?)f.Value)
                        .OrderByDescending(f => f)
                        .FirstOrDefault();


                foreach (var asig
                         in asignaciones)
                {
                    DateTime? fAsig =
                        ParsearFecha(
                            asig.FechaAsignado);

                    DateTime? fComp =
                        ParsearFecha(
                            asig.FechaCompletado);

                    DateTime? fUltimaCompJson =
                        ParsearFecha(
                            asig.UltimaFechaCompletado);


                    // Las dos estadísticas de "trabajados" deben usar las
                    // mismas fuentes de fecha. FechaCompletado es la devolución
                    // del registro actual y UltimaFechaCompletado puede contener
                    // una devolución anterior que el S-13 no trae como fila
                    // independiente.
                    bool completadoEnAnio =
                        (fComp.HasValue
                         && fComp.Value.Date >= inicioAnioServicio.Date
                         && fComp.Value.Date <= fechaCorte.Date)
                        ||
                        (fUltimaCompJson.HasValue
                         && fUltimaCompJson.Value.Date >= inicioAnioServicio.Date
                         && fUltimaCompJson.Value.Date <= fechaCorte.Date);

                    if (completadoEnAnio)
                    {
                        trabajadoEnAnio = true;
                    }

                    bool completadoEn6Meses =
                        (fComp.HasValue
                         && fComp.Value.Date >= inicioUltimos6Meses.Date
                         && fComp.Value.Date <= fechaCorte.Date)
                        ||
                        (fUltimaCompJson.HasValue
                         && fUltimaCompJson.Value.Date >= inicioUltimos6Meses.Date
                         && fUltimaCompJson.Value.Date <= fechaCorte.Date);

                    if (completadoEn6Meses)
                    {
                        trabajadoEn6Meses = true;
                    }


                    if (fAsig.HasValue
                        &&
                        fAsig.Value.Date <=
                        fechaCorte.Date
                        &&
                        (
                            !fComp.HasValue
                            ||
                            fComp.Value.Date >
                            fechaCorte.Date
                        ))
                    {
                        enCurso =
                            true;

                        if (fAsig.Value.Date <=
                            fecha4MesesAntes.Date)
                        {
                            caducado =
                                true;
                        }
                    }


                    if (fComp.HasValue
                        &&
                        fComp.Value.Date <=
                        fechaCorte.Date)
                    {
                        if (!ultimaDevolucionValida
                                .HasValue
                            ||
                            fComp.Value >
                            ultimaDevolucionValida
                                .Value)
                        {
                            ultimaDevolucionValida =
                                fComp.Value;
                        }
                    }
                }


                if (trabajadoEn6Meses)
                {
                    trabajados6Meses++;
                }

                if (trabajadoEnAnio)
                {
                    trabajadosAnioServicio++;
                }

                if (enCurso)
                {
                    trabajandoseAhora++;
                }

                if (caducado)
                {
                    caducados++;
                }


                // Para inactividad usamos la última devolución real conocida.
                // No usamos FechaCreacion porque puede ser únicamente la fecha
                // de importación en SCCosmos y falsearía los >1 / >2 años.
                DateTime fechaReferenciaParaInactividad =
                    ultimaDevolucionValida
                    ??
                    DateTime.MinValue;

                bool territorioSinTrabajar1Ano = false;
                bool territorioSinTrabajar2Anos = false;

                if (fechaReferenciaParaInactividad
                    !=
                    DateTime.MinValue)
                {
                    double diasInactivo =
                        (
                            fechaCorte
                            -
                            fechaReferenciaParaInactividad
                        )
                        .TotalDays;

                    if (diasInactivo >=
                        730)
                    {
                        territorioSinTrabajar2Anos = true;
                        sinTrabajar2Anos++;
                    }
                    else if (diasInactivo >=
                             365)
                    {
                        territorioSinTrabajar1Ano = true;
                        sinTrabajar1Ano++;
                    }
                }
                else
                {
                    territorioSinTrabajar2Anos = true;
                    sinTrabajar2Anos++;
                }

                // Guardamos en la propia fila las mismas condiciones que se
                // usan para los contadores. Así, al pulsar una tarjeta, el
                // filtro muestra exactamente los territorios que la componen.
                fila.TrabajadoUltimos6Meses = trabajadoEn6Meses;
                fila.TrabajadoAnioServicio = trabajadoEnAnio;
                fila.TrabajandoseEnFechaCorte = enCurso;
                fila.Caducado = caducado;
                fila.SinTrabajar1Ano = territorioSinTrabajar1Ano;
                fila.SinTrabajar2Anos = territorioSinTrabajar2Anos;
            }


            // filasGrid ya se ha construido siguiendo el primer Id de cada
            // territorio en TerritoriosVisitaV3. No aplicamos ningún orden
            // alfabético ni numérico adicional.
            _territoriosGlobal =
                filasGrid.ToList();

            ActualizarEstadoTarjetasFiltroTerritorios();

            // Conservamos la página actual al refrescar los territorios.
            // MostrarPaginaTerritorios ya limita automáticamente el número
            // si la página actual deja de existir.
            MostrarPaginaTerritorios(
                _paginaActual);


            if (TxtTotalTerritorios != null)
            {
                TxtTotalTerritorios.Text =
                    totalTerritorios.ToString();
            }

            if (TxtTerritoriosTrabajados != null)
            {
                TxtTerritoriosTrabajados.Text =
                    trabajados6Meses.ToString();
            }

            if (TxtTerritoriosTrabajadosAnio != null)
            {
                TxtTerritoriosTrabajadosAnio.Text =
                    trabajadosAnioServicio.ToString();
            }

            if (TxtTerritoriosEnCurso != null)
            {
                TxtTerritoriosEnCurso.Text =
                    trabajandoseAhora.ToString();
            }

            if (TxtTerritoriosCaducados != null)
            {
                TxtTerritoriosCaducados.Text =
                    caducados.ToString();
            }

            if (TxtTerritorios1Ano != null)
            {
                TxtTerritorios1Ano.Text =
                    sinTrabajar1Ano.ToString();
            }

            if (TxtTerritorios2Anos != null)
            {
                TxtTerritorios2Anos.Text =
                    sinTrabajar2Anos.ToString();
            }
        }


        private bool AsignacionIntersecaRango(
            TerritorioJSON asignacion,
            DateTime inicio,
            DateTime fin)
        {
            DateTime? fechaAsignado =
                ParsearFecha(
                    asignacion.FechaAsignado);

            DateTime? fechaCompletado =
                ParsearFecha(
                    asignacion.FechaCompletado);


            if (!fechaAsignado.HasValue
                &&
                !fechaCompletado.HasValue)
            {
                return false;
            }


            DateTime inicioRegistro =
                fechaAsignado
                ??
                fechaCompletado.Value;

            DateTime finRegistro =
                fechaCompletado
                ??
                fin;


            return
                inicioRegistro.Date <=
                fin.Date
                &&
                finRegistro.Date >=
                inicio.Date;
        }


        private List<FilaTablaTerritorio> ObtenerTerritoriosFiltrados()
        {
            IEnumerable<FilaTablaTerritorio> consulta =
                _territoriosGlobal ?? new List<FilaTablaTerritorio>();

            switch (_filtroTerritoriosActivo)
            {
                case "Ultimos6Meses":
                    consulta = consulta.Where(t => t.TrabajadoUltimos6Meses);
                    break;

                case "AnioServicio":
                    consulta = consulta.Where(t => t.TrabajadoAnioServicio);
                    break;

                case "EnCurso":
                    consulta = consulta.Where(t => t.TrabajandoseEnFechaCorte);
                    break;

                case "Caducados":
                    consulta = consulta.Where(t => t.Caducado);
                    break;

                case "SinTrabajar1Ano":
                    consulta = consulta.Where(t => t.SinTrabajar1Ano);
                    break;

                case "SinTrabajar2Anos":
                    consulta = consulta.Where(t => t.SinTrabajar2Anos);
                    break;
            }

            return consulta.ToList();
        }

        private void CardFiltroTerritorio_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!(sender is Border card))
                return;

            string filtro = card.Tag?.ToString() ?? "";

            // La tarjeta Nº Territorios funciona también como "mostrar todos".
            if (string.Equals(filtro, "Todos", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(_filtroTerritoriosActivo))
                {
                    _filtroTerritoriosActivo = "";
                    _paginaActual = Math.Max(1, _paginaAntesFiltroTerritorios);
                }
            }
            else if (string.Equals(
                         _filtroTerritoriosActivo,
                         filtro,
                         StringComparison.Ordinal))
            {
                // Segundo clic sobre la misma tarjeta: desmarcar.
                _filtroTerritoriosActivo = "";
                _paginaActual = Math.Max(1, _paginaAntesFiltroTerritorios);
            }
            else
            {
                // Al activar el primer filtro recordamos en qué página estaba
                // el listado completo. Cambiar de una tarjeta a otra no la pisa.
                if (string.IsNullOrEmpty(_filtroTerritoriosActivo))
                {
                    _paginaAntesFiltroTerritorios = _paginaActual;
                }

                _filtroTerritoriosActivo = filtro;
                _paginaActual = 1;
            }

            ActualizarEstadoTarjetasFiltroTerritorios();
            MostrarPaginaTerritorios(_paginaActual);
            e.Handled = true;
        }

        private void ActualizarEstadoTarjetasFiltroTerritorios()
        {
            ActualizarTarjetaFiltroTerritorio(
                CardTotalTerritorios,
                string.IsNullOrEmpty(_filtroTerritoriosActivo),
                "#B0B0B0");

            ActualizarTarjetaFiltroTerritorio(
                CardTerritoriosTrabajados6M,
                _filtroTerritoriosActivo == "Ultimos6Meses",
                "#32CD32");

            ActualizarTarjetaFiltroTerritorio(
                CardTerritoriosTrabajadosAnio,
                _filtroTerritoriosActivo == "AnioServicio",
                "#32CD32");

            ActualizarTarjetaFiltroTerritorio(
                CardTerritoriosEnCurso,
                _filtroTerritoriosActivo == "EnCurso",
                "#1E90FF");

            ActualizarTarjetaFiltroTerritorio(
                CardTerritoriosCaducados,
                _filtroTerritoriosActivo == "Caducados",
                "#FF8C00");

            ActualizarTarjetaFiltroTerritorio(
                CardTerritorios1Ano,
                _filtroTerritoriosActivo == "SinTrabajar1Ano",
                "#FF4500");

            ActualizarTarjetaFiltroTerritorio(
                CardTerritorios2Anos,
                _filtroTerritoriosActivo == "SinTrabajar2Anos",
                "#E6192B");
        }

        private static void ActualizarTarjetaFiltroTerritorio(
            Border tarjeta,
            bool activa,
            string color)
        {
            if (tarjeta == null)
                return;

            if (activa)
            {
                tarjeta.BorderThickness = new Thickness(2);
                tarjeta.BorderBrush =
                    new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(color));
            }
            else
            {
                tarjeta.BorderThickness = new Thickness(1);
                tarjeta.BorderBrush =
                    new SolidColorBrush(
                        Color.FromArgb(32, 255, 255, 255));
            }
        }

        private void MostrarPaginaTerritorios(int pagina)
        {
            var territoriosMostrar = ObtenerTerritoriosFiltrados();

            // Si el filtro activo no tiene coincidencias, limpiamos la tabla
            // pero mantenemos la tarjeta seleccionada para que quede claro qué
            // se está buscando.
            if (territoriosMostrar == null || !territoriosMostrar.Any())
            {
                _paginaActual = 1;
                if (TablaTerritorios != null) TablaTerritorios.ItemsSource = null;
                if (PanelPaginacion != null) PanelPaginacion.Children.Clear();
                return;
            }

            int totalPaginas =
                (int)Math.Ceiling(
                    territoriosMostrar.Count /
                    (double)_itemsPorPagina);

            if (pagina < 1) pagina = 1;
            if (pagina > totalPaginas) pagina = totalPaginas;

            _paginaActual = pagina;

            var datosPagina =
                territoriosMostrar
                    .Skip((pagina - 1) * _itemsPorPagina)
                    .Take(_itemsPorPagina)
                    .ToList();

            TablaTerritorios.ItemsSource = datosPagina;

            PanelPaginacion.Children.Clear();

            for (int i = 1; i <= totalPaginas; i++)
            {
                Button btn = new Button
                {
                    Content = i.ToString(),
                    Tag = i,
                    Style = (Style)this.FindResource("EstiloBotonPaginacion"),
                    Background = i == _paginaActual
                        ? (SolidColorBrush)new BrushConverter().ConvertFrom("#0078D4")
                        : (SolidColorBrush)new BrushConverter().ConvertFrom("#333333")
                };

                btn.Click += BtnPaginaTerritorio_Click;
                PanelPaginacion.Children.Add(btn);
            }
        }

        private void BtnPaginaTerritorio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int paginaTarget) MostrarPaginaTerritorios(paginaTarget);
        }

        private void AjustarAlturaMapaSegura(double altoPagina = 0)
        {
            if (ContenedorMapa == null) return;

            // No forzamos UpdateLayout ni TransformToAncestor. WebBrowser es un HWND
            // nativo y recalcular el layout desde SizeChanged/selección puede provocar
            // un bucle de layout y dejar la interfaz bloqueada.
            double altoBase = 0;

            if (MainScrollViewer != null && MainScrollViewer.ViewportHeight > 0)
                altoBase = MainScrollViewer.ViewportHeight;

            if (altoBase <= 0 && altoPagina > 0)
                altoBase = altoPagina;

            if (altoBase <= 0)
                altoBase = ActualHeight;

            if (altoBase <= 0) return;

            // Reservamos únicamente el título, la cabecera de pestañas y los márgenes.
            // Así el mapa ocupa prácticamente todo el área visible sin depender de
            // coordenadas del árbol visual.
            double altoMapa = Math.Max(320, altoBase - 115);

            if (double.IsNaN(ContenedorMapa.Height) ||
                Math.Abs(ContenedorMapa.Height - altoMapa) > 1)
            {
                ContenedorMapa.Height = altoMapa;
            }
        }

        private void RestablecerScrollPestana()
        {
            if (MainScrollViewer == null) return;

            MainScrollViewer.ScrollToHome();

            // Algunos controles (especialmente TextBox) solicitan BringIntoView al
            // materializar una pestaña. Repetimos el ajuste después del layout y
            // cuando la cola visual queda en reposo.
            Dispatcher.BeginInvoke(
                new Action(() => MainScrollViewer?.ScrollToHome()),
                DispatcherPriority.Loaded);

            Dispatcher.BeginInvoke(
                new Action(() => MainScrollViewer?.ScrollToHome()),
                DispatcherPriority.ApplicationIdle);
        }

        private void TabNotas_RequestBringIntoView(
            object sender,
            RequestBringIntoViewEventArgs e)
        {
            // Evita que un TextBox de Notas desplace el ScrollViewer exterior al
            // recuperar foco o al crearse su contenedor visual. Las pestañas quedan
            // siempre visibles en la parte superior.
            if (TabsVisita != null && ReferenceEquals(TabsVisita.SelectedItem, TabNotas))
            {
                e.Handled = true;
            }
        }

        private void TabsVisita_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // SelectionChanged es un evento enrutado: los ListBox/ComboBox que viven
            // dentro de las pestañas también pueden hacerlo burbujear hasta aquí.
            // Solo reaccionamos cuando cambia realmente la pestaña principal.
            if (TabsVisita == null || !ReferenceEquals(e.OriginalSource, TabsVisita))
                return;

            if (ReferenceEquals(TabsVisita.SelectedItem, TabNotas))
            {
                // No permitimos que un TextBox recuerde el foco y solicite
                // automáticamente desplazarse al hacerse visible la pestaña.
                Keyboard.ClearFocus();
            }

            RestablecerScrollPestana();

            if (ReferenceEquals(TabsVisita.SelectedItem, TabMapa))
            {
                TabMapa_Selected(TabMapa, new RoutedEventArgs());
            }
            else
            {
                TabMapa_Unselected(TabMapa, new RoutedEventArgs());
            }
        }

        private async void TabMapa_Selected(
            object sender,
            RoutedEventArgs e)
        {
            // WebBrowser es un HWND nativo. Lo mantenemos oculto hasta que la
            // pestaña esté materializada, pero evitamos forzar UpdateLayout porque
            // puede entrar en un ciclo de relayout y bloquear la ventana.
            MapaWebBrowser.Visibility = Visibility.Collapsed;
            AjustarAlturaMapaSegura();

            await Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.Loaded);

            if (TabsVisita == null || TabsVisita.SelectedItem != TabMapa)
                return;

            AjustarAlturaMapaSegura();
            MapaWebBrowser.Visibility = Visibility.Visible;

            if (_mapaCargado
                &&
                !string.IsNullOrEmpty(
                    _htmlMapaCache))
            {
                MapaWebBrowser.NavigateToString(
                    _htmlMapaCache);

                return;
            }

            var personasActivas = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo);
            // Si la latitud es 999, es que ya la buscamos y no existe, no la volvemos a buscar
            var pendientes = personasActivas.Where(p => !string.IsNullOrWhiteSpace(p.Direccion) && p.Latitud == 0 && p.Longitud == 0).ToList();

            if (pendientes.Count > 0)
            {
                string htmlCarga = $@"
                <!DOCTYPE html>
                <html><head>
                    <meta charset='UTF-8'>
                    <style>body{{margin:0;padding:0;overflow:hidden;background:#252526;color:#4BA3E3;display:flex;flex-direction:column;justify-content:center;align-items:center;height:100vh;font-family:'Segoe UI',sans-serif;text-align:center;}}</style>
                </head><body>
                    <h2>Geolocalizando direcciones en el mapa...</h2>
                    <p>Convirtiendo textos a coordenadas ({pendientes.Count} pendientes).</p>
                </body></html>";

                MapaWebBrowser.NavigateToString(htmlCarga);

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 SCCosmos/1.0");

                    foreach (var p in pendientes)
                    {
                        try
                        {
                            string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(p.Direccion)}&format=json&limit=1&countrycodes=es";
                            string response = await client.GetStringAsync(url);
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var resultados = JsonSerializer.Deserialize<List<NominatimResult>>(response, options);

                            double lat = 999, lon = 999;

                            if (resultados != null && resultados.Count > 0)
                            {
                                if (double.TryParse(resultados[0].lat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double latP)) lat = latP;
                                if (double.TryParse(resultados[0].lon, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lonP)) lon = lonP;
                            }

                            p.Latitud = lat;
                            p.Longitud = lon;

                            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabaseHelper.RutaBaseDatos};Pooling=True"))
                            {
                                connection.Open();
                                var cmd = connection.CreateCommand();
                                cmd.CommandText = "UPDATE Personas SET Latitud=@lat, Longitud=@lon WHERE Id=@id OR PersonaRaizId=@id OR Id=(SELECT PersonaRaizId FROM Personas WHERE Id=@id)";
                                cmd.Parameters.AddWithValue("@lat", lat);
                                cmd.Parameters.AddWithValue("@lon", lon);
                                cmd.Parameters.AddWithValue("@id", p.Id);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch { break; }
                        await Task.Delay(1500);
                    }
                }
            }
            DibujarMapa(personasActivas);
        }

        private void TabMapa_Unselected(object sender, RoutedEventArgs e)
        {
            MapaWebBrowser.Visibility = Visibility.Collapsed;
        }

        private void DibujarMapa(List<Persona> personas)
        {
            var sb = new System.Text.StringBuilder();
            var personasConCoords = personas.Where(p => p.Latitud != 0 && p.Latitud != 999 && p.Longitud != 0 && p.Longitud != 999).ToList();

            foreach (var p in personasConCoords)
            {
                string nombreLimpio = EscapeJsString(p.Nombre);
                string dirLimpia = EscapeJsString(p.Direccion ?? "");
                sb.AppendLine($"var m = L.marker([{p.Latitud.ToString(CultureInfo.InvariantCulture)}, {p.Longitud.ToString(CultureInfo.InvariantCulture)}]).addTo(map).bindPopup('<b>{nombreLimpio}</b><br>{dirLimpia}');");
                sb.AppendLine("markers.push(m);");
            }

            var congre = DatabaseHelper.ObtenerCongregaciones().FirstOrDefault(c => c.Codigo == _congreCodigo);
            if (congre != null && congre.Latitud != 0 && congre.Longitud != 0)
            {
                string nombreLimpio = EscapeJsString(congre.Nombre);
                string dirLimpia = EscapeJsString(congre.DireccionSR ?? "");

                sb.AppendLine($@"
                    var srIcon = new L.Icon({{
                      iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-red.png',
                      shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/0.7.7/images/marker-shadow.png',
                      iconSize: [25, 41],
                      iconAnchor: [12, 41],
                      popupAnchor: [1, -34],
                      shadowSize: [41, 41]
                    }});
                    var mSR = L.marker([{congre.Latitud.ToString(CultureInfo.InvariantCulture)}, {congre.Longitud.ToString(CultureInfo.InvariantCulture)}], {{icon: srIcon}}).addTo(map).bindPopup('<b style=\'color:red\'>SALÓN DEL REINO</b><br><b>{nombreLimpio}</b><br>{dirLimpia}');
                    markers.push(mSR);
                ");
            }

            var elementosMapa = DatabaseHelper.ObtenerElementosMapa();
            var territoriosDeEstaCongre = elementosMapa.Where(e => e.Tipo == "Polygon" &&
                (e.Nombre.IndexOf(_congreNombre, StringComparison.OrdinalIgnoreCase) >= 0 || _congreNombre.IndexOf(e.Nombre, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            foreach (var terr in territoriosDeEstaCongre)
            {
                string n = EscapeJsString(terr.Nombre);
                string desc = EscapeJsString(terr.Descripcion.Replace("\n", "<br>"));
                sb.AppendLine($"var poly = L.polygon({terr.Coordenadas}, {{color: '#4BA3E3', weight: 2, fillOpacity: 0.15}}).addTo(map).bindPopup('<div style=\\'max-height:250px; overflow-y:auto; font-family:Segoe UI;\\'><h3 style=\\'margin-top:0px; margin-bottom:5px; color:#1e1e1e;\\'>{n}</h3>{desc}</div>');");
                sb.AppendLine("polygons.push(poly);");
            }

            string html = $@"
            <!DOCTYPE html>
            <html><head>
                <meta http-equiv='X-UA-Compatible' content='IE=edge' />
                <meta charset='UTF-8'>
                <link rel='stylesheet' href='https://unpkg.com/leaflet@1.7.1/dist/leaflet.css' />
                <script src='https://unpkg.com/leaflet@1.7.1/dist/leaflet.js'></script>
                <style>body{{margin:0;padding:0;overflow:hidden;background:#252526;font-family:Segoe UI, sans-serif;}} #map{{height:100vh;width:100vw;background:#252526;}}</style>
            </head><body>
                <div id='map'></div>
                <script>
                    var map = L.map('map').setView([40.4168, -3.7038], 6);
                    L.tileLayer('https://{{s}}.tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png', {{ attribution: '© OpenStreetMap contributors' }}).addTo(map);
                    
                    var markers = [];
                    var polygons = [];
                    
                    {sb.ToString()}
                    
                    var bounds = L.latLngBounds();
                    markers.forEach(function(m) {{ bounds.extend(m.getLatLng()); }});
                    polygons.forEach(function(p) {{ bounds.extend(p.getBounds()); }});
                    
                    if (bounds.isValid()) {{
                        map.fitBounds(bounds, {{padding: [30, 30]}});
                    }}
                </script>
            </body></html>";

            _htmlMapaCache = html;
            _mapaCargado = true;
            MapaWebBrowser.NavigateToString(html);
        }

        private string EscapeJsString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "").Replace("\u2028", "").Replace("\u2029", "");
        }

        // =====================================
        // INTEGRACIÓN AVANZADA DE FORMULARIO
        // =====================================

        private void TablaVisitas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (TablaVisitas == null) return;

            // Los botones de Editar/Eliminar siguen teniendo su propia acción.
            // Si el clic procede de uno de ellos no abrimos dos veces el formulario.
            DependencyObject origen = e.OriginalSource as DependencyObject;
            DependencyObject actual = origen;
            while (actual != null)
            {
                if (actual is Button) return;
                if (actual is DataGridRow filaVisual)
                {
                    if (filaVisual.Item is FilaVisitaPublisher fila)
                    {
                        int personaRaizId = fila.PersonaRaizId > 0
                            ? fila.PersonaRaizId
                            : fila.PersonaId;

                        AbrirFormularioPersona(personaRaizId);
                        e.Handled = true;
                    }
                    return;
                }

                actual = VisualTreeHelper.GetParent(actual);
            }
        }

        private void BtnEditarPersona_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn
                && btn.DataContext is FilaVisitaPublisher fila)
            {
                // VISITAS abre la ficha raíz para conservar actividad y vínculos.
                // Al guardar, ActualizarPersonaDesdeVisitas actualiza también
                // la instantánea correspondiente al mes de esta visita.
                int personaRaizId =
                    fila.PersonaRaizId > 0
                        ? fila.PersonaRaizId
                        : fila.PersonaId;

                AbrirFormularioPersona(
                    personaRaizId);
            }
        }

        private async void BtnGenerarTarjetasS21_Click(object sender, RoutedEventArgs e)
        {
            int? anioServicio = SolicitarAnioServicioS21();
            if (!anioServicio.HasValue) return;

            string rutaPlantilla = LocalizarPlantillaS21();
            if (string.IsNullOrWhiteSpace(rutaPlantilla)) return;

            var idsPersonas = _filasVisitasGlobal
                .OrderBy(f => f.NombreComun ?? f.Nombre ?? "", StringComparer.CurrentCultureIgnoreCase)
                .Select(f => f.PersonaRaizId > 0 ? f.PersonaRaizId : f.PersonaId)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (idsPersonas.Count == 0)
            {
                MessageBox.Show("No hay publicadores en la tabla de Ministerio para generar tarjetas S-21.",
                    "S-21", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlgGuardar = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar tarjetas S-21",
                Filter = "Documento PDF (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = $"S-21_{SanitizarNombreArchivo(_congreNombre)}_{anioServicio.Value}.pdf"
            };

            if (dlgGuardar.ShowDialog() != true) return;

            Button boton = sender as Button;
            if (boton != null) boton.IsEnabled = false;
            if (PanelCargando != null) PanelCargando.Visibility = Visibility.Visible;

            try
            {
                int total = await Task.Run(() =>
                    GenerarPdfS21Masivo(
                        rutaPlantilla,
                        dlgGuardar.FileName,
                        anioServicio.Value,
                        idsPersonas));

                MessageBox.Show(
                    $"Se han generado {total} tarjetas S-21 para el año de servicio {anioServicio.Value}.\n\n{dlgGuardar.FileName}",
                    "S-21 generado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"No se pudieron generar las tarjetas S-21:\n{ex.Message}",
                    "Error S-21",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (PanelCargando != null) PanelCargando.Visibility = Visibility.Collapsed;
                if (boton != null) boton.IsEnabled = true;
            }
        }

        private int? SolicitarAnioServicioS21()
        {
            int sugerido = ObtenerAnioServicio(DateTime.Today);
            int? resultado = null;

            var dialogo = new Window
            {
                Title = "Generar tarjetas S-21",
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = Brushes.White,
                ShowInTaskbar = false
            };

            var contenedor = new StackPanel { Margin = new Thickness(22), Width = 330 };
            contenedor.Children.Add(new TextBlock
            {
                Text = "Año de servicio a rellenar",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            });
            contenedor.Children.Add(new TextBlock
            {
                Text = "Ejemplo: 2027 corresponde de septiembre de 2026 a agosto de 2027.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var txt = new TextBox
            {
                Text = sugerido.ToString(),
                FontSize = 16,
                Padding = new Thickness(10, 7, 10, 7),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 74))
            };
            contenedor.Children.Add(txt);

            var botones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var cancelar = new Button
            {
                Content = "Cancelar",
                MinWidth = 90,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };

            var aceptar = new Button
            {
                Content = "Generar",
                MinWidth = 90,
                Padding = new Thickness(12, 6, 12, 6),
                IsDefault = true
            };

            aceptar.Click += (s, e) =>
            {
                if (!int.TryParse(txt.Text?.Trim(), out int anio) || anio < 1900 || anio > 2200)
                {
                    MessageBox.Show("Introduce un año de servicio válido.", "S-21", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txt.Focus();
                    txt.SelectAll();
                    return;
                }

                resultado = anio;
                dialogo.DialogResult = true;
            };

            botones.Children.Add(cancelar);
            botones.Children.Add(aceptar);
            contenedor.Children.Add(botones);
            dialogo.Content = contenedor;
            dialogo.Loaded += (s, e) => { txt.Focus(); txt.SelectAll(); };
            dialogo.ShowDialog();

            return resultado;
        }

        private string LocalizarPlantillaS21()
        {
            string[] candidatas =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "S-21_S.pdf"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plantillas", "S-21_S.pdf"),
                Path.Combine(DatabaseHelper.RutaDirectorioDatos, "S-21_S.pdf"),
                Path.Combine(DatabaseHelper.RutaDirectorioDatos, "Plantillas", "S-21_S.pdf")
            };

            string encontrada = candidatas.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(encontrada)) return encontrada;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecciona la plantilla S-21-S",
                Filter = "Documento PDF (*.pdf)|*.pdf",
                Multiselect = false
            };

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private int GenerarPdfS21Masivo(
            string rutaPlantilla,
            string rutaSalida,
            int anioServicio,
            List<int> idsPersonas)
        {
            string plantillaPreparada = PrepararPlantillaS21(rutaPlantilla);
            var temporales = new List<string>();
            int generadas = 0;

            try
            {
                using (var pdfSalida = new PdfDocument(new PdfWriter(rutaSalida)))
                {
                    foreach (int idPersona in idsPersonas)
                    {
                        Persona persona = DatabaseHelper.ObtenerPersonaPorId(idPersona);
                        if (persona == null) continue;

                        var actividad = DatabaseHelper.ObtenerActividadPersona(idPersona)
                            .Where(a => string.Equals(a.AnioServicio?.Trim(), anioServicio.ToString(), StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        string tempPersona = Path.Combine(
                            Path.GetTempPath(),
                            $"SCCosmos_S21_{Guid.NewGuid():N}.pdf");
                        temporales.Add(tempPersona);

                        using (var pdfPersona = new PdfDocument(
                            new PdfReader(plantillaPreparada),
                            new PdfWriter(tempPersona)))
                        {
                            PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfPersona, true);
                            if (form == null || form.GetField("900_1_Text_SanSerif") == null)
                                throw new InvalidOperationException("La plantilla S-21 no contiene los campos esperados.");

                            RellenarTarjetaS21(form, persona, actividad, anioServicio);
                            form.FlattenFields();
                        }

                        using (var pdfPersonaLectura = new PdfDocument(new PdfReader(tempPersona)))
                        {
                            pdfPersonaLectura.CopyPagesTo(
                                1,
                                pdfPersonaLectura.GetNumberOfPages(),
                                pdfSalida);
                        }

                        generadas++;
                    }
                }
            }
            finally
            {
                foreach (string temp in temporales)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }

                try
                {
                    if (!string.Equals(plantillaPreparada, rutaPlantilla, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(plantillaPreparada))
                    {
                        File.Delete(plantillaPreparada);
                    }
                }
                catch { }
            }

            return generadas;
        }

        private string PrepararPlantillaS21(string rutaPlantilla)
        {
            string temp = Path.Combine(
                Path.GetTempPath(),
                $"SCCosmos_S21_Plantilla_{Guid.NewGuid():N}.pdf");

            using (var pdf = new PdfDocument(
                new PdfReader(rutaPlantilla),
                new PdfWriter(temp)))
            {
                PdfAcroForm form = PdfAcroForm.GetAcroForm(pdf, true);
                if (form == null)
                    throw new InvalidOperationException("El PDF seleccionado no contiene un formulario S-21 válido.");

                // Algunas versiones oficiales del S-21 contienen widgets con nombre,
                // pero el array /Fields del AcroForm viene vacío. Registramos esos
                // widgets para que iText pueda tratarlos como campos normales.
                if (form.GetField("900_1_Text_SanSerif") == null)
                {
                    PdfArray fields = form.GetPdfObject().GetAsArray(PdfName.Fields);
                    if (fields == null)
                    {
                        fields = new PdfArray();
                        form.GetPdfObject().Put(PdfName.Fields, fields);
                    }

                    for (int pagina = 1; pagina <= pdf.GetNumberOfPages(); pagina++)
                    {
                        foreach (PdfAnnotation anotacion in pdf.GetPage(pagina).GetAnnotations())
                        {
                            PdfDictionary dic = anotacion.GetPdfObject();
                            bool esWidget = PdfName.Widget.Equals(dic.GetAsName(PdfName.Subtype));
                            bool tieneNombre = dic.GetAsString(PdfName.T) != null;
                            bool tieneTipo = dic.GetAsName(PdfName.FT) != null;

                            if (esWidget && tieneNombre && tieneTipo && dic.GetIndirectReference() != null)
                                fields.Add(dic.GetIndirectReference());
                        }
                    }
                }
            }

            return temp;
        }

        private void RellenarTarjetaS21(
            PdfAcroForm form,
            Persona persona,
            List<ActividadMensual> actividad,
            int anioServicio)
        {
            PonerTextoS21(form, "900_1_Text_SanSerif", persona.Nombre ?? "");
            PonerTextoS21(form, "900_2_Text_SanSerif", FormatearFechaS21(persona.FechaNacimiento));
            PonerTextoS21(form, "900_5_Text_SanSerif", FormatearFechaS21(persona.FechaBautismo));

            MarcarS21(form, "900_3_CheckBox", string.Equals(persona.Sexo, "Hombre", StringComparison.OrdinalIgnoreCase));
            MarcarS21(form, "900_4_CheckBox", string.Equals(persona.Sexo, "Mujer", StringComparison.OrdinalIgnoreCase));

            string nombramiento = persona.Nombramiento ?? "";
            string servicio = persona.Servicio ?? "";

            MarcarS21(form, "900_8_CheckBox", nombramiento.IndexOf("anciano", StringComparison.OrdinalIgnoreCase) >= 0);
            MarcarS21(form, "900_9_CheckBox", nombramiento.IndexOf("siervo", StringComparison.OrdinalIgnoreCase) >= 0 || nombramiento.IndexOf("ministerial", StringComparison.OrdinalIgnoreCase) >= 0);
            MarcarS21(form, "900_10_CheckBox", servicio.IndexOf("precursor regular", StringComparison.OrdinalIgnoreCase) >= 0);
            MarcarS21(form, "900_11_CheckBox", servicio.IndexOf("precursor especial", StringComparison.OrdinalIgnoreCase) >= 0);
            MarcarS21(form, "900_12_CheckBox", servicio.IndexOf("misionero", StringComparison.OrdinalIgnoreCase) >= 0);

            PonerTextoS21(form, "900_13_Text_C_SanSerif", anioServicio.ToString());

            string[] meses =
            {
                "septiembre", "octubre", "noviembre", "diciembre",
                "enero", "febrero", "marzo", "abril",
                "mayo", "junio", "julio", "agosto"
            };

            for (int i = 0; i < meses.Length; i++)
            {
                int numeroCampo = 20 + i;
                var registro = actividad.FirstOrDefault(a =>
                    string.Equals(a.Mes?.Trim(), meses[i], StringComparison.OrdinalIgnoreCase));

                bool participo = registro != null &&
                    (registro.Participacion || HorasS21MayoresQueCero(registro.Horas));

                MarcarS21(form, $"901_{numeroCampo}_CheckBox", participo);
                PonerTextoS21(form, $"902_{numeroCampo}_Text_C_SanSerif", registro?.CursosBiblicos ?? "");
                MarcarS21(form, $"903_{numeroCampo}_CheckBox", registro?.PrecursorAuxiliar == true);
                PonerTextoS21(form, $"904_{numeroCampo}_S21_Value", registro?.Horas ?? "");
                PonerTextoS21(form, $"905_{numeroCampo}_Text_SanSerif", registro?.Notas ?? "");
            }
        }

        private static bool HorasS21MayoresQueCero(string horas)
        {
            if (string.IsNullOrWhiteSpace(horas)) return false;

            return double.TryParse(
                       horas.Trim().Replace(",", "."),
                       NumberStyles.Any,
                       CultureInfo.InvariantCulture,
                       out double valor)
                   && valor > 0;
        }

        private string FormatearFechaS21(string fecha)
        {
            DateTime? valor = ParsearFecha(fecha);
            return valor.HasValue ? valor.Value.ToString("dd/MM/yyyy") : (fecha?.Trim() ?? "");
        }

        private static void PonerTextoS21(PdfAcroForm form, string nombreCampo, string valor)
        {
            var campo = form.GetField(nombreCampo);
            if (campo != null)
                campo.SetValue(valor ?? "");
        }

        private static void MarcarS21(PdfAcroForm form, string nombreCampo, bool marcado)
        {
            var campo = form.GetField(nombreCampo);
            if (campo != null)
                campo.SetValue(marcado ? "Yes" : "Off");
        }

        private static string SanitizarNombreArchivo(string texto)
        {
            string valor = string.IsNullOrWhiteSpace(texto) ? "Congregacion" : texto.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                valor = valor.Replace(c, '_');
            return valor;
        }

        private void BtnNuevaPersona_Click(object sender, RoutedEventArgs e)
        {
            AbrirFormularioPersona(0);
        }

        private void AbrirFormularioPersona(int personaId)
        {
            DateTime fechaActual = _mesMinisterio;

            Persona persona = null;
            if (personaId > 0)
            {
                persona = DatabaseHelper.ObtenerPersonaPorId(personaId);
            }
            else
            {
                persona = new Persona { CongregacionCodigo = _congreCodigo, Nombre = "Nuevo Publicador" };
                int nuevoId = DatabaseHelper.InsertarPersonaNueva(persona);
                persona.Id = nuevoId;
            }

            if (persona != null)
            {
                // Los candidatos de vinculación también deben ser fichas raíz,
                // nunca IDs de instantáneas históricas.
                var personasActuales =
                    DatabaseHelper
                        .ObtenerPersonasPorCongregacion(
                            _congreCodigo)
                        .ToDictionary(
                            p => p.Id,
                            p => p);

                var idsMes =
                    _filasVisitasGlobal
                        .Select(
                            f =>
                                f.PersonaRaizId > 0
                                    ? f.PersonaRaizId
                                    : f.PersonaId)
                        .Distinct()
                        .ToHashSet();

                var listaPersonasEsteMes =
                    personasActuales
                        .Where(
                            kv =>
                                idsMes.Contains(
                                    kv.Key))
                        .Select(
                            kv => kv.Value)
                        .ToList();

                var ventana = new FormularioPersonaWindow(persona, listaPersonasEsteMes);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    if (!ventana.EsUnaFusion()) DatabaseHelper.ActualizarPersonaDesdeVisitas(ventana.PersonaActualizada, fechaActual.ToString("yyyy-MM"));

                    // --- NUEVO: Vaciamos la caché del mapa para obligarlo a recargar y buscar la nueva dirección ---
                    _mapaCargado = false;
                    _htmlMapaCache = "";

                    ActualizarUI_MesMinisterio();

                    // Territorios muestra los mismos publicadores y nombres actuales.
                    ActualizarUI_MesTerritorios();
                }
                else if (personaId == 0)
                {
                    DatabaseHelper.EliminarPersona(persona.Id);
                }
            }
        }

        private void BtnEliminarPersona_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FilaVisitaPublisher fila)
            {
                var resultado = MessageBox.Show($"¿Estás seguro de que deseas eliminar permanentemente a {fila.Nombre}?\nSe borrará también todo su historial de actividad.", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (resultado == MessageBoxResult.Yes)
                {
                    // En meses históricos la fila puede corresponder a una instantánea.
                    // Debemos borrar siempre la persona raíz para que se eliminen también
                    // sus copias históricas y su actividad.
                    int personaRaizId =
                        fila.PersonaRaizId > 0
                            ? fila.PersonaRaizId
                            : fila.PersonaId;

                    DatabaseHelper.EliminarPersona(personaRaizId);

                    // Quitamos también la fila de la caché visual inmediatamente.
                    _filasVisitasGlobal.RemoveAll(
                        f =>
                            f.PersonaId == personaRaizId
                            || f.PersonaRaizId == personaRaizId);

                    // Vaciamos la caché del mapa para que desaparezca la chincheta.
                    _mapaCargado = false;
                    _htmlMapaCache = "";

                    ActualizarUI_MesMinisterio();
                }
            }
        }

        // =====================================
        // RESTO DEL CÓDIGO (Territorios y Cursos)
        // =====================================

        private void CmbVinculacion_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            // Ignoramos los SelectionChanged que genera la propia carga de
            // Territorios, pero no los bloqueamos porque Ministerio esté cargando.
            if (_cargandoTerritorios) return;

            if (sender is ComboBox combo
                && combo.DataContext is CeldaVuelta celda)
            {
                if (!combo.IsLoaded) return;
                if (!combo.IsDropDownOpen
                    && !combo.IsKeyboardFocusWithin)
                {
                    return;
                }

                string nombreSeleccionado = "";

                if (e.AddedItems.Count > 0
                    && e.AddedItems[0] is OpcionPersonaTerritorio opcion)
                {
                    nombreSeleccionado = opcion.NombreS21;
                }
                else if (combo.SelectedValue is string valorSeleccionado)
                {
                    nombreSeleccionado = valorSeleccionado;
                }

                if (string.IsNullOrWhiteSpace(nombreSeleccionado))
                    return;

                string nombreGuardar =
                    nombreSeleccionado == "-- Sin Asignar --"
                        ? ""
                        : nombreSeleccionado;

                DatabaseHelper.ActualizarVinculacionTerritorio(
                    celda.RegistroId,
                    nombreGuardar);

                celda.NombreS21 = nombreGuardar;

                ActualizarUI_MesTerritorios();
                // El contador "Terrs." de Ministerio depende de esta vinculación.
                // Lo recalculamos inmediatamente para que ambas pestañas queden sincronizadas.
                ActualizarUI_MesMinisterio();
            }
        }

        private void CmbVinculacion_KeyUp(
            object sender,
            KeyEventArgs e)
        {
            if (_cargandoTerritorios) return;

            if (!(sender is ComboBox combo)
                || !(combo.DataContext is CeldaVuelta celda))
            {
                return;
            }

            // Las teclas de navegación/confirmación no cambian el filtro.
            // Así podemos movernos por los resultados y seleccionar con Enter
            // sin que el desplegable se reconstruya continuamente.
            if (e.Key == Key.Up
                || e.Key == Key.Down
                || e.Key == Key.Left
                || e.Key == Key.Right
                || e.Key == Key.Enter
                || e.Key == Key.Tab
                || e.Key == Key.Escape)
            {
                return;
            }

            var txt =
                combo.Template.FindName(
                    "PART_EditableTextBox",
                    combo) as TextBox;

            if (txt == null || !txt.IsKeyboardFocusWithin)
                return;

            string busqueda = (txt.Text ?? "").Trim();

            ICollectionView vista =
                CollectionViewSource.GetDefaultView(
                    celda.ListaPersonasS21);

            if (string.IsNullOrWhiteSpace(busqueda))
            {
                vista.Filter = null;
            }
            else
            {
                vista.Filter = obj =>
                {
                    if (!(obj is OpcionPersonaTerritorio p))
                        return false;

                    // Dejamos siempre disponible la opción de quitar la
                    // asignación y buscamos por cualquiera de los nombres.
                    return
                        p.NombreS21 == "-- Sin Asignar --"
                        || ContieneTextoTerritorio(p.NombreComun, busqueda)
                        || ContieneTextoTerritorio(p.NombreJWHub, busqueda)
                        || ContieneTextoTerritorio(p.NombreS21, busqueda)
                        || ContieneTextoTerritorio(p.NombreVisible, busqueda);
                };
            }

            vista.Refresh();

            // No tocamos txt.Text ni ItemsSource. Solo abrimos el desplegable.
            // Esto evita el bucle de TextChanged que provocaba las pulsaciones
            // y búsquedas aparentemente "a lo loco".
            combo.IsDropDownOpen = true;
        }

        private static List<OpcionPersonaTerritorio> OrdenarPersonasTerritorioPorSimilitud(
            IEnumerable<OpcionPersonaTerritorio> personas,
            string nombreReferencia)
        {
            var resultado = new List<OpcionPersonaTerritorio>();

            var sinAsignar = personas
                .FirstOrDefault(p => p.NombreS21 == "-- Sin Asignar --");

            if (sinAsignar != null)
            {
                resultado.Add(new OpcionPersonaTerritorio
                {
                    NombreS21 = sinAsignar.NombreS21,
                    NombreComun = sinAsignar.NombreComun,
                    NombreJWHub = sinAsignar.NombreJWHub,
                    Coincidencia = -1
                });
            }

            var ordenadas = personas
                .Where(p => p.NombreS21 != "-- Sin Asignar --")
                .Select(p =>
                {
                    int similitudNombreCompleto =
                        CalcularSimilitudTerritorio(nombreReferencia, p.NombreS21);
                    int similitudNombreComun =
                        CalcularSimilitudTerritorio(nombreReferencia, p.NombreComun);
                    int similitudJWHub =
                        CalcularSimilitudTerritorio(nombreReferencia, p.NombreJWHub);

                    return new OpcionPersonaTerritorio
                    {
                        NombreS21 = p.NombreS21,
                        NombreComun = p.NombreComun,
                        NombreJWHub = p.NombreJWHub,
                        Coincidencia = Math.Max(
                            similitudNombreCompleto,
                            Math.Max(similitudNombreComun, similitudJWHub))
                    };
                })
                .OrderByDescending(p => p.Coincidencia)
                .ThenBy(p => p.NombreVisible, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            resultado.AddRange(ordenadas);
            return resultado;
        }

        private static int CalcularSimilitudTerritorio(
            string referencia,
            string candidato)
        {
            string a = NormalizarNombreTerritorio(referencia);
            string b = NormalizarNombreTerritorio(candidato);

            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0;

            if (a == b)
                return 100;

            // Igual que en FormularioPersonaWindow: combinamos parecido directo,
            // parecido con las palabras ordenadas y coincidencia por tokens.
            double directa = SimilitudLevenshteinTerritorio(a, b);

            string tokensA = string.Join(
                " ",
                a.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .OrderBy(x => x));

            string tokensB = string.Join(
                " ",
                b.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .OrderBy(x => x));

            double porTokensOrdenados =
                SimilitudLevenshteinTerritorio(tokensA, tokensB);

            var setA = new HashSet<string>(
                a.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            var setB = new HashSet<string>(
                b.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            int union = setA.Union(setB).Count();
            int interseccion = setA.Intersect(setB).Count();

            double coincidenciaTokens =
                union == 0
                    ? 0
                    : (double)interseccion / union;

            double mejor =
                Math.Max(
                    directa,
                    Math.Max(porTokensOrdenados, coincidenciaTokens));

            return Math.Max(
                0,
                Math.Min(100, (int)Math.Round(mejor * 100)));
        }

        private static string NormalizarNombreTerritorio(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return "";

            string descompuesto =
                texto.Trim()
                    .ToLowerInvariant()
                    .Normalize(System.Text.NormalizationForm.FormD);

            var sb = new System.Text.StringBuilder();
            bool ultimoEraEspacio = false;

            foreach (char c in descompuesto)
            {
                UnicodeCategory categoria =
                    CharUnicodeInfo.GetUnicodeCategory(c);

                if (categoria == UnicodeCategory.NonSpacingMark)
                    continue;

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

            return sb.ToString()
                .Trim()
                .Normalize(System.Text.NormalizationForm.FormC);
        }

        private static double SimilitudLevenshteinTerritorio(
            string a,
            string b)
        {
            if (a == b)
                return 1.0;

            if (a.Length == 0 || b.Length == 0)
                return 0.0;

            int[] anterior = new int[b.Length + 1];
            int[] actual = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
                anterior[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                actual[0] = i;

                for (int j = 1; j <= b.Length; j++)
                {
                    int coste = a[i - 1] == b[j - 1] ? 0 : 1;

                    actual[j] = Math.Min(
                        Math.Min(
                            actual[j - 1] + 1,
                            anterior[j] + 1),
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

        private static bool ContieneTextoTerritorio(
            string texto,
            string busqueda)
        {
            if (string.IsNullOrWhiteSpace(busqueda))
                return true;

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            return CultureInfo
                       .GetCultureInfo("es-ES")
                       .CompareInfo
                       .IndexOf(
                           texto,
                           busqueda,
                           CompareOptions.IgnoreCase
                           | CompareOptions.IgnoreNonSpace)
                   >= 0;
        }

        private void BtnEliminarRegistroTerritorio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CeldaVuelta celda && celda.RegistroId > 0)
            {
                string nombre = string.IsNullOrWhiteSpace(celda.NombreS13)
                    ? "(sin nombre)"
                    : celda.NombreS13;

                string fechas = string.IsNullOrWhiteSpace(celda.Fechas)
                    ? "sin fechas"
                    : celda.Fechas;

                var resultado = MessageBox.Show(
                    $"¿Quieres borrar este registro de territorio?\n\n" +
                    $"Nombre original: {nombre}\n" +
                    $"Fechas: {fechas}\n\n" +
                    "Solo se eliminará este registro concreto. El resto de registros del territorio no se modificarán.",
                    "Borrar registro de territorio",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (resultado == MessageBoxResult.Yes)
                {
                    DatabaseHelper.EliminarRegistroTerritorio(celda.RegistroId);

                    // Conservamos el filtro y la página actuales siempre que
                    // sigan existiendo después de eliminar el registro.
                    ActualizarUI_MesTerritorios();
                    ActualizarUI_MesMinisterio();
                }

                e.Handled = true;
            }
        }

        private void BtnEliminarTerritorio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FilaTablaTerritorio fila)
            {
                if (MessageBox.Show($"¿Estás seguro de eliminar el territorio '{fila.Nombre}'?\n\nNo se borrarán los datos del pasado, pero dejará de aparecer y contabilizar a partir de este mes.", "Eliminar Territorio", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    DatabaseHelper.MarcarTerritorioEliminado(_congreCodigo, fila.Nombre);
                    ActualizarUI_MesTerritorios();
                    ActualizarUI_MesMinisterio();
                }
            }
        }

        private void BtnEditarTerritorio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FilaTablaTerritorio fila)
            {
                var inputWin = new Window { Title = "Renombrar Territorio", Width = 350, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterScreen, WindowStyle = WindowStyle.ToolWindow, Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)) };
                var grid = new StackPanel { Margin = new Thickness(20) };
                grid.Children.Add(new TextBlock { Text = "Nuevo nombre del territorio:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 10) });

                var txt = new TextBox { Text = fila.Nombre, Padding = new Thickness(8), FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
                grid.Children.Add(txt);

                var btnSave = new Button { Content = "Guardar Cambios", Width = 120, Margin = new Thickness(0, 15, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)), Foreground = Brushes.White, Padding = new Thickness(8), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };

                btnSave.Click += (s, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(txt.Text))
                    {
                        DatabaseHelper.RenombrarTerritorioLocal(_congreCodigo, fila.Nombre, txt.Text.Trim());
                        inputWin.DialogResult = true;
                        inputWin.Close();
                    }
                };

                grid.Children.Add(btnSave);
                inputWin.Content = grid;

                if (inputWin.ShowDialog() == true) ActualizarUI_MesTerritorios();
            }
        }

        private void CargarContabilidadList(DateTime mesReferencia)
        {
            var historial = new List<FilaContabilidad>();
            string[] nombresMesesJson = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

            // La contabilidad se muestra por año de servicio completo:
            // septiembre del año anterior hasta agosto del año indicado.
            int anioServicio = ObtenerAnioServicio(mesReferencia);
            DateTime inicioAnioServicio = new DateTime(anioServicio - 1, 9, 1);

            for (int i = 0; i < 12; i++)
            {
                DateTime m = inicioAnioServicio.AddMonths(i);
                string mStr = m.ToString("yyyy-MM");
                string mVisual = char.ToUpper(nombresMesesJson[m.Month - 1][0]) + nombresMesesJson[m.Month - 1].Substring(1) + " " + m.Year;

                var datosDB = DatabaseHelper.ObtenerContabilidad(_congreCodigo, mStr);

                historial.Add(new FilaContabilidad
                {
                    MesReferencia = mStr,
                    NombreMesVisual = mVisual,
                    Gastos = FormatearMonedaContabilidad(datosDB.Gastos),
                    Mantenimiento = FormatearMonedaContabilidad(datosDB.Mantenimiento),
                    ObraMundial = FormatearMonedaContabilidad(datosDB.ObraMundial),
                    ObraResolucion = FormatearMonedaContabilidad(datosDB.ObraResolucion),
                    SaldoMaximo = FormatearMonedaContabilidad(datosDB.SaldoMaximo),
                    Saldo = FormatearMonedaContabilidad(datosDB.Saldo),
                    PublicadoresActivos = CalcularPublicadoresActivosContabilidad(m)
                });
            }

            TablaContabilidad.ItemsSource = historial;
        }

        private int CalcularPublicadoresActivosContabilidad(
            DateTime mes)
        {
            try
            {
                DateTime mesReferencia =
                    new DateTime(
                        mes.Year,
                        mes.Month,
                        1);

                var personas =
                    DatabaseHelper
                        .ObtenerPersonasHistoricasPorMes(
                            _congreCodigo,
                            mesReferencia.ToString("yyyy-MM"));

                int total = 0;

                foreach (Persona persona in personas)
                {
                    if (persona == null
                        || persona.Grupo <= 0)
                    {
                        continue;
                    }

                    DateTime? fechaEliminacion =
                        ParsearFecha(
                            persona.FechaEliminacion);

                    if (fechaEliminacion.HasValue
                        &&
                        new DateTime(
                            fechaEliminacion.Value.Year,
                            fechaEliminacion.Value.Month,
                            1)
                        < mesReferencia)
                    {
                        continue;
                    }

                    DateTime? fechaCreacion =
                        ParsearFecha(
                            persona.FechaCreacion);

                    bool sinCuentaJW =
                        string.IsNullOrWhiteSpace(
                            persona.NombreJWHub)
                        &&
                        string.IsNullOrWhiteSpace(
                            persona.EmailJW);

                    if (!sinCuentaJW
                        &&
                        fechaCreacion.HasValue
                        &&
                        new DateTime(
                            fechaCreacion.Value.Year,
                            fechaCreacion.Value.Month,
                            1)
                        > mesReferencia)
                    {
                        continue;
                    }

                    total++;
                }

                return total;
            }
            catch
            {
                return _totalPublicadoresActivos;
            }
        }

        private static string FormatearMonedaContabilidad(
            double valor)
        {
            return
                valor.ToString(
                    "N2",
                    CultureInfo.GetCultureInfo("es-ES"))
                + " €";
        }

        private void TablaContabilidad_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Row.Item is FilaContabilidad fila)
                {
                    fila.NormalizarMonedas();
                    fila.ForzarActualizacionPromedio();

                    var datos = new DatabaseHelper.ContabilidadMes
                    {
                        Gastos = ParsearMoneda(fila.Gastos),
                        Mantenimiento = ParsearMoneda(fila.Mantenimiento),
                        ObraMundial = ParsearMoneda(fila.ObraMundial),
                        ObraResolucion = ParsearMoneda(fila.ObraResolucion),
                        SaldoMaximo = ParsearMoneda(fila.SaldoMaximo),
                        Saldo = ParsearMoneda(fila.Saldo)
                    };

                    DatabaseHelper.GuardarContabilidad(
                        _congreCodigo,
                        fila.MesReferencia,
                        datos);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private double ParsearMoneda(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return 0;
            }

            string limpio =
                texto
                    .Replace("€", "")
                    .Replace("\u00A0", " ")
                    .Trim();

            if (double.TryParse(
                    limpio,
                    NumberStyles.Number,
                    CultureInfo.GetCultureInfo("es-ES"),
                    out double valorEs))
            {
                return valorEs;
            }

            if (double.TryParse(
                    limpio.Replace(",", "."),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double valorInv))
            {
                return valorInv;
            }

            return 0;
        }

        private (string Valor, string Notas, Brush ColorValor, string Cursos) LocalizarActividadMes(List<ActividadMensual> historico, DateTime fechaTarget)
        {
            string[] nombresMesesJson = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
            string mesBuscado = nombresMesesJson[fechaTarget.Month - 1];
            string anioServicioBuscado = (fechaTarget.Month >= 9) ? (fechaTarget.Year + 1).ToString() : fechaTarget.Year.ToString();

            var brushConv = new BrushConverter();
            var colorBlanco = Brushes.White;
            var colorVerde = (Brush)brushConv.ConvertFrom("#55C855");
            var colorRojo = (Brush)brushConv.ConvertFrom("#FF5555");
            var colorAzulAux = (Brush)brushConv.ConvertFrom("#0078D4");
            var colorAmaAux = (Brush)brushConv.ConvertFrom("#FFB900");

            var registro = historico.FirstOrDefault(i => i.Mes.ToLower() == mesBuscado && i.AnioServicio == anioServicioBuscado);
            if (registro == null) return ("-", null, colorBlanco, "");

            string notasDelMes = string.IsNullOrWhiteSpace(registro.Notas) ? null : registro.Notas.Trim();
            string valorVisual = "❌";
            double horasNumericas = 0;
            Brush colorFinal = colorBlanco;

            string hStr = (registro.Horas ?? "").Replace(" ", "").Replace("\t", "").Trim();

            if (!string.IsNullOrEmpty(hStr) && hStr != "0" && hStr != "0.0" && hStr != "0,0")
            {
                valorVisual = hStr;
                double.TryParse(hStr.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out horasNumericas);
            }
            else if (registro.Participacion) { valorVisual = "✅"; colorFinal = colorVerde; }
            else colorFinal = colorRojo;

            if (notasDelMes != null) valorVisual += "*";

            if (registro.PrecursorAuxiliar && horasNumericas > 0)
            {
                if (horasNumericas >= 15) colorFinal = colorAzulAux;
                else colorFinal = colorAmaAux;
            }

            string cursosMes = (registro.CursosBiblicos ?? "").Replace(" ", "").Trim();

            return (valorVisual, notasDelMes, colorFinal, cursosMes);
        }

        /// <summary>
        /// Horas acumuladas del año de servicio hasta <paramref name="mes"/>
        /// y color: rojo si van por debajo del ritmo del requisito
        /// (50 h/mes los regulares; cuota mensual especiales/misioneros).
        /// Devuelve texto vacío si la persona no tiene requisito anual.
        /// </summary>
        private static (string Texto, Brush Color) CrearAcumuladoMes(
            List<ActividadMensual> informes,
            DateTime mes,
            double metaMensual,
            bool esPrecursorRegular,
            DateTime? inicioPrecursor)
        {
            if (metaMensual <= 0)
            {
                return ("", Brushes.Transparent);
            }

            string[] nombres =
            {
                "enero", "febrero", "marzo", "abril", "mayo", "junio",
                "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
            };

            string anio =
                (mes.Month >= 9 ? mes.Year + 1 : mes.Year).ToString();

            int orden =
                mes.Month >= 9 ? mes.Month - 8 : mes.Month + 4;

            double acumulado = 0;

            foreach (ActividadMensual act in informes)
            {
                if (!string.Equals(act.AnioServicio, anio, StringComparison.Ordinal))
                {
                    continue;
                }

                int numero =
                    Array.IndexOf(
                        nombres,
                        (act.Mes ?? "").Trim().ToLowerInvariant()) + 1;

                if (numero <= 0)
                {
                    continue;
                }

                int ordenAct =
                    numero >= 9 ? numero - 8 : numero + 4;

                if (ordenAct > orden)
                {
                    continue;
                }

                string horas =
                    (act.Horas ?? "").Replace(" ", "").Replace(",", ".");

                if (double.TryParse(
                        horas,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double valor)
                    && valor > 0)
                {
                    acumulado += valor;
                }
            }

            double meta =
                metaMensual * orden;

            if (esPrecursorRegular && inicioPrecursor.HasValue)
            {
                DateTime inicioMes =
                    new(inicioPrecursor.Value.Year, inicioPrecursor.Value.Month, 1);

                DateTime mesActual =
                    new(mes.Year, mes.Month, 1);

                if (mesActual < inicioMes)
                {
                    meta = 0;
                }
                else
                {
                    DateTime inicioAnio =
                        mes.Month >= 9
                            ? new DateTime(mes.Year, 9, 1)
                            : new DateTime(mes.Year - 1, 9, 1);

                    DateTime desde =
                        inicioMes > inicioAnio ? inicioMes : inicioAnio;

                    int meses =
                        ((mesActual.Year - desde.Year) * 12)
                        + mesActual.Month
                        - desde.Month
                        + 1;

                    meta =
                        metaMensual * Math.Max(0, Math.Min(orden, meses));
                }
            }

            string texto =
                Math.Round(acumulado, 1)
                    .ToString("0.#", CultureInfo.CurrentCulture);

            Brush color =
                acumulado < meta
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0xA3))
                    : new SolidColorBrush(Color.FromRgb(0x9E, 0xA4, 0xAE));

            return (texto, color);
        }


        private bool EsFechaEnRango(string fechaStr, DateTime inicio, DateTime fin)
        {
            if (string.IsNullOrWhiteSpace(fechaStr)) return false;
            DateTime? fecha = ParsearFecha(fechaStr);
            if (fecha.HasValue) return fecha.Value >= inicio && fecha.Value <= fin;
            return false;
        }

        private CeldaVuelta CrearCeldaVuelta(
            TerritorioJSON asig,
            List<OpcionPersonaTerritorio> personasS21,
            Dictionary<string, string> cacheFuzzy)
        {
            var celda =
                new CeldaVuelta();

            if (asig == null || string.IsNullOrWhiteSpace(asig.AsignadoA))
            {
                celda.ListaPersonasS21 = personasS21;
                return celda;
            }

            // Cada registro de S-13 recibe su propia lista de candidatos,
            // ordenada de mayor a menor parecido con el nombre del registro.
            // Las opciones se clonan para que el porcentaje sea específico
            // de este registro y no se comparta con otras filas.
            celda.ListaPersonasS21 =
                OrdenarPersonasTerritorioPorSimilitud(
                    personasS21,
                    asig.AsignadoA);

            celda.EsVisible = Visibility.Visible;
            celda.RegistroId = asig.Id;
            celda.NombreS13 = asig.AsignadoA;
            celda.Fechas =
                $"{(string.IsNullOrWhiteSpace(asig.FechaAsignado) ? "--" : asig.FechaAsignado)} " +
                $"al {(string.IsNullOrWhiteSpace(asig.FechaCompletado) ? "--" : asig.FechaCompletado)}";

            if (!string.IsNullOrWhiteSpace(asig.NombreVinculado)
                && personasS21.Any(
                    p =>
                        string.Equals(
                            p.NombreS21,
                            asig.NombreVinculado,
                            StringComparison.OrdinalIgnoreCase)))
            {
                celda.NombreS21 = asig.NombreVinculado;
            }
            else if (cacheFuzzy.ContainsKey(asig.AsignadoA))
            {
                celda.NombreS21 = cacheFuzzy[asig.AsignadoA];
            }
            else
            {
                string mejorMatch = "";
                int mayorSim = 0;
                int menorDif = 999;

                foreach (var persona in celda.ListaPersonasS21.Skip(1))
                {
                    int similitudNombre =
                        CalcularSimilitudTerritorio(
                            asig.AsignadoA,
                            persona.NombreS21);

                    int similitudNombreComun =
                        CalcularSimilitudTerritorio(
                            asig.AsignadoA,
                            persona.NombreComun);

                    int similitudJWHub =
                        CalcularSimilitudTerritorio(
                            asig.AsignadoA,
                            persona.NombreJWHub);

                    int sim =
                        Math.Max(
                            similitudNombre,
                            Math.Max(
                                similitudNombreComun,
                                similitudJWHub));

                    int dif =
                        Math.Abs(
                            asig.AsignadoA.Length
                            - persona.NombreS21.Length);

                    if (sim > mayorSim
                        || (sim == mayorSim && dif < menorDif))
                    {
                        mayorSim = sim;
                        menorDif = dif;
                        mejorMatch = persona.NombreS21;
                    }
                }

                if (mayorSim >= 50)
                {
                    celda.NombreS21 = mejorMatch;
                    cacheFuzzy[asig.AsignadoA] = mejorMatch;
                    DatabaseHelper.ActualizarVinculacionTerritorio(
                        asig.Id,
                        mejorMatch);
                }
                else
                {
                    cacheFuzzy[asig.AsignadoA] = "";
                }
            }

            return celda;
        }

        private DateTime? ParsearFecha(string fechaStr)
        {
            if (string.IsNullOrWhiteSpace(fechaStr)) return null;
            if (DateTime.TryParseExact(fechaStr.Trim(), "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime dtExact)) return dtExact;
            if (DateTime.TryParse(fechaStr.Trim(), out DateTime dtFlexible)) return dtFlexible;
            return null;
        }

        

        private void BtnEditarS303_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)
                || !(btn.Tag is DocumentoS303UI docUI)
                || docUI.DocumentoOriginal == null)
            {
                return;
            }

            try
            {
                string rutaOrigen =
                    DatabaseHelper.ExtraerDocumentoDesdeBD(
                        docUI.DocumentoOriginal.Id,
                        docUI.DocumentoOriginal.NombreArchivo);

                if (string.IsNullOrWhiteSpace(rutaOrigen)
                    || !File.Exists(rutaOrigen))
                {
                    MessageBox.Show(
                        "No se ha podido recuperar el PDF desde la base de datos.",
                        "S-303",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                if (docUI.EsPlantilla)
                {
                    string mesRef = _mesS303.ToString("yyyy-MM");
                    string nombreCopia = $"S-303_{_congreCodigo}_{mesRef}.pdf";

                    string carpetaEdicion =
                        Path.Combine(
                            Path.GetTempPath(),
                            "SCCosmos",
                            "S303Edicion",
                            _congreCodigo ?? "Congregacion",
                            mesRef);

                    Directory.CreateDirectory(carpetaEdicion);

                    string rutaCopia =
                        Path.Combine(
                            carpetaEdicion,
                            nombreCopia);

                    // OBTENER DATOS PARA AUTO-COMPLETAR
                    var congre =
                        DatabaseHelper
                            .ObtenerCongregaciones()
                            .FirstOrDefault(c =>
                                c.Codigo == _congreCodigo);

                    string fechaVisitaStr = "";

                    using (var conn =
                           new Microsoft.Data.Sqlite.SqliteConnection(
                               $"Data Source={DatabaseHelper.RutaBaseDatos};Pooling=True"))
                    {
                        conn.Open();

                        var cmd = conn.CreateCommand();
                        cmd.CommandText =
                            "SELECT FechaMartes FROM Semanas WHERE Congregacion = @c AND Actividad = 'Visita' AND FechaMartes LIKE @m || '%' LIMIT 1";
                        cmd.Parameters.AddWithValue("@c", _congreNombre);
                        cmd.Parameters.AddWithValue("@m", mesRef);

                        var res = cmd.ExecuteScalar();

                        if (res != null
                            &&
                            DateTime.TryParse(
                                res.ToString(),
                                out DateTime fMartes))
                        {
                            DateTime fDom =
                                fMartes.AddDays(5);

                            fechaVisitaStr =
                                $"Del {fMartes.Day} de {fMartes.ToString("MMM", new CultureInfo("es-ES"))} "
                                + $"al {fDom.Day} de {fDom.ToString("MMM", new CultureInfo("es-ES"))} de {fDom.Year}";
                        }
                    }

                    string nomSC =
                        DatabaseHelper
                            .ObtenerConfiguracion(
                                "NombreSC",
                                "")
                            .Trim();

                    string apeSC =
                        DatabaseHelper.ObtenerConfiguracion(
                            "ApellidosSC",
                            "");

                    if (string.IsNullOrEmpty(apeSC))
                    {
                        apeSC =
                            DatabaseHelper.ObtenerConfiguracion(
                                "ApellidoSC",
                                "");
                    }

                    string nombreCompletoSC =
                        $"{nomSC} {apeSC}".Trim();

                    string numCirc =
                        DatabaseHelper.ObtenerConfiguracion(
                            "NumeroCircuito",
                            "");

                    using (PdfDocument pdfDoc =
                           new PdfDocument(
                               new PdfReader(rutaOrigen),
                               new PdfWriter(rutaCopia)))
                    {
                        PdfAcroForm form =
                            PdfAcroForm.GetAcroForm(
                                pdfDoc,
                                true);

                        if (form != null)
                        {
                            float tamanoLetra = 10f;

                            var f1 = form.GetField("900_1_Text_SanSerif");
                            if (f1 != null)
                            {
                                f1.SetFontSize(tamanoLetra);
                                f1.SetValue(_congreCodigo ?? "");
                            }

                            var f2 = form.GetField("900_2_Text_SanSerif");
                            if (f2 != null)
                            {
                                f2.SetFontSize(tamanoLetra);
                                f2.SetValue(fechaVisitaStr ?? "");
                            }

                            var f3 = form.GetField("900_3_Text_SanSerif");
                            if (f3 != null)
                            {
                                f3.SetFontSize(tamanoLetra);
                                f3.SetValue(_congreNombre ?? "");
                            }

                            var f4 = form.GetField("900_4_Text_SanSerif");
                            if (f4 != null)
                            {
                                f4.SetFontSize(tamanoLetra);
                                f4.SetValue(congre?.Municipio ?? "");
                            }

                            var f5 = form.GetField("900_5_Text_SanSerif");
                            if (f5 != null)
                            {
                                f5.SetFontSize(tamanoLetra);
                                f5.SetValue(congre?.Provincia ?? "");
                            }

                            var f6 = form.GetField("900_6_Text_SanSerif");
                            if (f6 != null)
                            {
                                f6.SetFontSize(tamanoLetra);
                                f6.SetValue(nombreCompletoSC);
                            }

                            var f7 = form.GetField("900_7_Text_SanSerif");
                            if (f7 != null)
                            {
                                f7.SetFontSize(tamanoLetra);
                                f7.SetValue(numCirc ?? "");
                            }
                        }
                    }

                    // La copia se guarda inmediatamente como BLOB.
                    DatabaseHelper.InsertarDocumento(
                        "S-303",
                        nombreCopia,
                        rutaCopia);

                    ArchivoDocumento documentoCreado =
                        DatabaseHelper
                            .ObtenerDocumentos("S-303")
                            .Where(d =>
                                string.Equals(
                                    d.NombreArchivo,
                                    nombreCopia,
                                    StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(d => d.Id)
                            .FirstOrDefault();

                    if (documentoCreado != null)
                    {
                        string rutaEditable =
                            DatabaseHelper.ExtraerDocumentoDesdeBD(
                                documentoCreado.Id,
                                documentoCreado.NombreArchivo)
                            ?? rutaCopia;

                        AbrirS303YGuardarAlVolver(
                            documentoCreado,
                            rutaEditable);
                    }

                    ActualizarUI_MesS303();
                }
                else
                {
                    AbrirS303YGuardarAlVolver(
                        docUI.DocumentoOriginal,
                        rutaOrigen);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo abrir o preparar el S-303.\n\n"
                    + ex.Message,
                    "S-303",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void AbrirS303YGuardarAlVolver(
            ArchivoDocumento documento,
            string rutaArchivo)
        {
            if (documento == null
                || documento.Id <= 0
                || string.IsNullOrWhiteSpace(rutaArchivo)
                || !File.Exists(rutaArchivo))
            {
                return;
            }

            Window ventana =
                Window.GetWindow(this);

            EventHandler guardarAlVolver = null;

            if (ventana != null)
            {
                guardarAlVolver =
                    (s, args) =>
                    {
                        ventana.Activated -= guardarAlVolver;

                        Dispatcher.BeginInvoke(
                            new Action(() =>
                            {
                                GuardarS303EnBaseDeDatos(
                                    documento,
                                    rutaArchivo,
                                    mostrarConfirmacion: false);
                            }),
                            DispatcherPriority.Background);
                    };

                ventana.Activated += guardarAlVolver;
            }

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(
                        rutaArchivo)
                    {
                        UseShellExecute = true
                    });
            }
            catch
            {
                if (ventana != null
                    && guardarAlVolver != null)
                {
                    ventana.Activated -= guardarAlVolver;
                }

                throw;
            }
        }

        private void BtnGuardarS303BD_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button btn
                &&
                btn.Tag is DocumentoS303UI docUI
                &&
                !docUI.EsPlantilla
                &&
                docUI.DocumentoOriginal != null)
            {
                string ruta =
                    docUI.DocumentoOriginal.RutaLocal;

                if (string.IsNullOrWhiteSpace(ruta)
                    || !File.Exists(ruta))
                {
                    ruta =
                        DatabaseHelper.ExtraerDocumentoDesdeBD(
                            docUI.DocumentoOriginal.Id,
                            docUI.DocumentoOriginal.NombreArchivo);
                }

                GuardarS303EnBaseDeDatos(
                    docUI.DocumentoOriginal,
                    ruta,
                    mostrarConfirmacion: true);
            }
        }

        private void GuardarS303EnBaseDeDatos(
            ArchivoDocumento documento,
            string rutaArchivo,
            bool mostrarConfirmacion)
        {
            try
            {
                if (documento == null
                    || documento.Id <= 0
                    || string.IsNullOrWhiteSpace(rutaArchivo)
                    || !File.Exists(rutaArchivo))
                {
                    throw new FileNotFoundException(
                        "No se encuentra la copia editable del S-303.");
                }

                DatabaseHelper.ActualizarDocumentoDesdeArchivo(
                    documento.Id,
                    rutaArchivo);

                if (mostrarConfirmacion)
                {
                    MessageBox.Show(
                        "El S-303 se ha guardado en la base de datos. Los cambios estarán disponibles en los demás dispositivos que usen esta BBDD.",
                        "S-303 guardado",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                CargarS303yS2();
            }
            catch (Exception ex)
            {
                if (mostrarConfirmacion)
                {
                    MessageBox.Show(
                        "No se pudieron guardar los cambios del S-303 en la base de datos.\n\n"
                        + ex.Message,
                        "S-303",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void BtnEliminarS303_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DocumentoS303UI docUI)
            {
                if (!docUI.EsPlantilla && docUI.DocumentoOriginal != null)
                {
                    if (MessageBox.Show("¿Eliminar el S-303 de este mes? Se perderán los datos que hubieras escrito en el PDF.", "Eliminar S-303", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        // 1. Borramos el archivo físico del disco duro
                        try { if (File.Exists(docUI.DocumentoOriginal.RutaLocal)) File.Delete(docUI.DocumentoOriginal.RutaLocal); } catch { }

                        // 2. Buscamos TODOS los registros en la base de datos asociados a este S-303 y los aniquilamos
                        string mesRef = _mesS303.ToString("yyyy-MM");
                        string identificadorVisita = $"S-303_{_congreCodigo}_{mesRef}";

                        var todosLosRegistros = DatabaseHelper.ObtenerDocumentos("S-303")
                            .Where(d => d.NombreArchivo.Contains(identificadorVisita))
                            .ToList();

                        foreach (var doc in todosLosRegistros)
                        {
                            DatabaseHelper.EliminarDocumento(doc.Id);
                        }

                        // 3. Vaciamos la lista por completo y la recargamos
                        ListaS303.ItemsSource = null;
                        CargarS303yS2();
                    }
                }
            }
        }

        private void CargarS303yS2()
        {
            // 1. Cargar S-303 inteligente (Busca copia o pide plantilla)
            var listaUI = new List<DocumentoS303UI>();
            string mesRef = _mesS303.ToString("yyyy-MM");
            string identificadorVisita = $"S-303_{_congreCodigo}_{mesRef}";

            var docEspecifico = DatabaseHelper.ObtenerDocumentos("S-303")
                .FirstOrDefault(d => d.NombreArchivo.Contains(identificadorVisita));

            if (docEspecifico != null)
            {
                listaUI.Add(new DocumentoS303UI
                {
                    NombreVisual = $"Informe S-303 - {_congreNombre} ({FormatearMes(_mesS303)})",
                    TextoBoton = "✏️ Abrir / Editar",
                    EsPlantilla = false,
                    DocumentoOriginal = docEspecifico
                });
            }
            else
            {
                // Se busca primero en el apartado nuevo del S-303 y, si aún no
                // hay nada, en la categoría clásica (archivos antiguos).
                var plantilla = DatabaseHelper.ObtenerDocumentos("Envío de S-303|S-303")
                    .Where(d => d.NombreArchivo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.Id)
                    .FirstOrDefault()
                    ?? DatabaseHelper.ObtenerDocumentos("Envío de S-303")
                        .FirstOrDefault(d => d.NombreArchivo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

                if (plantilla != null)
                {
                    listaUI.Add(new DocumentoS303UI
                    {
                        NombreVisual = $"Plantilla detectada: {plantilla.NombreArchivo}",
                        TextoBoton = "📄 Crear para este mes",
                        EsPlantilla = true,
                        DocumentoOriginal = plantilla
                    });
                }
            }

            if (listaUI.Count > 0)
            {
                ListaS303.ItemsSource = null;
                ListaS303.ItemsSource = listaUI;
                ListaS303.Visibility = Visibility.Visible;
                TxtSinS303.Visibility = Visibility.Collapsed;
            }
            else
            {
                ListaS303.Visibility = Visibility.Collapsed;
                TxtSinS303.Text = "No se ha subido la plantilla genérica del S-303.\nPor favor, súbela primero desde 'Correspondencia > Visitas a las congregaciones > Envío de S-303'.";
                TxtSinS303.Visibility = Visibility.Visible;
            }

            // 2. Cargar S-2 (Recomendaciones y bajas pendientes)
            RefrescarTablaRecomendacionesS2();

            // 3. Cargar la plantilla de correo por defecto
            string asuntoPlantilla = DatabaseHelper.ObtenerConfiguracion("Asunto_Envío de S-303", "Informe S-303");
            string cuerpoPlantilla = DatabaseHelper.ObtenerConfiguracion("Email_Envío de S-303", "Queridos hermanos,<br><br>Adjunto el informe S-303 de la visita.<br><br>Un saludo cariñoso.");

            // Reemplazamos etiquetas dinámicas
            asuntoPlantilla = asuntoPlantilla.Replace("[Congregacion]", _congreNombre);
            cuerpoPlantilla = cuerpoPlantilla.Replace("[Congregacion]", _congreNombre);

            TxtAsuntoEnvio.Text = asuntoPlantilla;

            // Inyectamos el HTML directamente en el editor visual
            WbEditorHelper.SetHtml(WbEditorEnvio, cuerpoPlantilla);
        }

        private void RefrescarTablaRecomendacionesS2()
        {
            var recomendaciones = DatabaseHelper.ObtenerRecomendaciones()
                // Incluir tanto nombramientos como bajas. Hub puede devolver
                // congregación con código, por lo que no se exige igualdad literal.
                .Where(r => EsRecomendacionDeCongregacionActual(r.Congregacion))
                .Select(r => new RecomendacionS2UI { Original = r, Decision = "No" })
                .ToList();

            if (recomendaciones.Count > 0)
            {
                TablaS2.ItemsSource = recomendaciones;
                TablaS2.Visibility = Visibility.Visible;
                TxtSinS2.Visibility = Visibility.Collapsed;
            }
            else
            {
                TablaS2.Visibility = Visibility.Collapsed;
                TxtSinS2.Visibility = Visibility.Visible;
            }

        }

        private bool EsRecomendacionDeCongregacionActual(string congregacionHub)
        {
            string recibido = NormalizarTextoCongregacion(congregacionHub);
            string actual = NormalizarTextoCongregacion(_congreNombre);
            if (string.IsNullOrEmpty(recibido) || string.IsNullOrEmpty(actual)) return false;
            if (recibido == actual) return true;

            // Hub suele añadir el número entre paréntesis a la congregación.
            string codigo = (_congreCodigo ?? "").Trim();
            if (!string.IsNullOrEmpty(codigo))
            {
                var sinCodigo = System.Text.RegularExpressions.Regex.Replace(
                    congregacionHub ?? "", @"\s*\(" + System.Text.RegularExpressions.Regex.Escape(codigo) + @"\)\s*$", "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (NormalizarTextoCongregacion(sinCodigo) == actual) return true;
            }
            return false;
        }

        private static string NormalizarTextoCongregacion(string valor)
        {
            string descompuesto = (valor ?? "").Trim().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(c));
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static bool EsBajaDeNombramiento(RecomendacionHub rec)
        {
            string texto = ((rec?.Tipo ?? "") + " " + (rec?.RecomendadoComo ?? "")).ToLowerInvariant();
            return texto.Contains("baja") || texto.Contains("cese") || texto.Contains("cesar")
                   || texto.Contains("remoci") || texto.Contains("relevo")
                   || texto.Contains("dejar de servir") || texto.Contains("deja de servir");
        }

        private string FirmaSeleccionCarta(IEnumerable<RecomendacionS2UI> decisiones)
        {
            return (_congreCodigo ?? "") + "|" + _mesS303.ToString("yyyy-MM") + "|" + DateTime.Today.ToString("yyyy-MM-dd") + "|" +
                   string.Join("|", decisiones.Where(d => d.Aprobar)
                       .Select(d => d.Original.UrlDetalle + ":" + d.Original.Hermano + ":" + d.Original.Tipo + ":" + d.Original.RecomendadoComo)
                       .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        private ArchivoDocumento ObtenerPlantillaCartaNombramientos()
        {
            // Apartado nuevo de la carta; si aún no hay nada ahí, se busca el
            // archivo clásico subido en la categoría simple.
            var nueva = DatabaseHelper.ObtenerDocumentos("Envío de S-303|Carta de nombramientos")
                .OrderByDescending(d => d.Id).FirstOrDefault();

            if (nueva != null) return nueva;

            return DatabaseHelper.ObtenerDocumentos("Envío de S-303")
                .Where(d => string.Equals(d.NombreArchivo?.Trim(), "Carta de nombramiento.docx", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.Id).FirstOrDefault();
        }

        private string PrepararCartaNombramientos(List<RecomendacionS2UI> decisiones, bool regenerar)
        {
            var firma = FirmaSeleccionCarta(decisiones);
            if (!regenerar && firma == _firmaCartaNombramientos &&
                !string.IsNullOrWhiteSpace(_cartaNombramientosEditable) && File.Exists(_cartaNombramientosEditable))
                return _cartaNombramientosEditable;

            ArchivoDocumento plantilla = ObtenerPlantillaCartaNombramientos();
            if (plantilla == null) throw new FileNotFoundException(
                "No se encuentra 'Carta de nombramiento.docx' en Correspondencia > Visitas a congregaciones > Envío de S-303.");

            string rutaOrigen = DatabaseHelper.ExtraerDocumentoDesdeBD(plantilla.Id, plantilla.NombreArchivo);
            if (string.IsNullOrWhiteSpace(rutaOrigen) || !File.Exists(rutaOrigen))
                throw new IOException("El archivo de la carta no tiene contenido accesible en la base de datos.");

            string directorio = Path.Combine(Path.GetTempPath(), "SCCosmos", "CartasNombramiento", _congreCodigo ?? "SinCongregacion");
            Directory.CreateDirectory(directorio);
            string ruta = Path.Combine(directorio, "Carta_nombramiento_" + _mesS303.ToString("yyyy-MM") + "_" + Guid.NewGuid().ToString("N") + ".docx");
            File.Copy(rutaOrigen, ruta, true);

            var aprobadas = decisiones.Where(d => d.Aprobar).Select(d => d.Original).ToList();
            string nombresSiervos = string.Join(", ", aprobadas.Where(r => !EsBajaDeNombramiento(r) &&
                ((r.RecomendadoComo ?? "") + " " + (r.Tipo ?? "")).IndexOf("siervo", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(r => r.Hermano).Distinct(StringComparer.OrdinalIgnoreCase));
            string nombresAncianos = string.Join(", ", aprobadas.Where(r => !EsBajaDeNombramiento(r) &&
                ((r.RecomendadoComo ?? "") + " " + (r.Tipo ?? "")).IndexOf("anciano", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(r => r.Hermano).Distinct(StringComparer.OrdinalIgnoreCase));
            string nombresBajas = string.Join(", ", aprobadas.Where(EsBajaDeNombramiento)
                .Select(r => r.Hermano).Distinct(StringComparer.OrdinalIgnoreCase));

            string nombreSC = DatabaseHelper.ObtenerConfiguracion("NombreSC", "").Trim();
            string apellidoSC = DatabaseHelper.ObtenerConfiguracion("ApellidosSC", "").Trim();
            if (string.IsNullOrEmpty(apellidoSC)) apellidoSC = DatabaseHelper.ObtenerConfiguracion("ApellidoSC", "").Trim();
            string circuito = DatabaseHelper.ObtenerConfiguracion("NombreCircuito", "").Trim();
            if (string.IsNullOrEmpty(circuito)) circuito = DatabaseHelper.ObtenerConfiguracion("Circuito", "").Trim();
            if (string.IsNullOrEmpty(circuito)) circuito = DatabaseHelper.ObtenerConfiguracion("NumeroCircuito", "").Trim();
            string congre = (_congreNombre ?? "").Trim() + (string.IsNullOrWhiteSpace(_congreCodigo) ? "" : " (" + _congreCodigo + ")");

            var valores = new Dictionary<string, string>
            {
                ["$_FECHA"] = DateTime.Today.ToString("dd/MM/yyyy", new CultureInfo("es-ES")),
                ["$_SC"] = (nombreSC + " " + apellidoSC).Trim(),
                ["$_CIRCUITO"] = circuito,
                ["$_SM"] = nombresSiervos,
                ["$_ANCIANOS"] = nombresAncianos,
                ["$_BAJAS"] = nombresBajas,
                ["$_CONGREGACION"] = congre
            };

            using (var documento = Xceed.Words.NET.DocX.Load(ruta))
            {
                foreach (var kv in valores.OrderByDescending(kv => kv.Key.Length))
                    documento.ReplaceText(kv.Key, kv.Value ?? "");
                documento.Save();
            }
            _cartaNombramientosEditable = ruta;
            _firmaCartaNombramientos = firma;
            return ruta;
        }

        private static IWebElement esperaArchivoParaCarta(IWebDriver driver, WebDriverWait wait)
        {
            return wait.Until(d => d.FindElements(By.XPath("//input[@type='file']")).FirstOrDefault());
        }

        private static string ConvertirCartaNombramientosAPdf(string rutaDocx)
        {
            string rutaPdf = DocumentoHelper.ConvertirAPdfSiEsWord(rutaDocx);
            if (string.IsNullOrWhiteSpace(rutaPdf) ||
                !string.Equals(Path.GetExtension(rutaPdf), ".pdf", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(rutaPdf) || new FileInfo(rutaPdf).Length == 0)
            {
                throw new IOException("No se ha conseguido convertir 'Carta de nombramiento.docx' a un PDF válido. " +
                                      "Comprueba que Word/LibreOffice esté disponible y cierra el documento antes de enviar.");
            }
            return rutaPdf;
        }

        private void BtnAbrirCartaNombramiento_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var filas = (TablaS2.ItemsSource as IEnumerable<RecomendacionS2UI>)?.ToList()
                            ?? new List<RecomendacionS2UI>();
                string ruta = PrepararCartaNombramientos(filas, false);
                Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
                TxtEstadoCarta.Text = "Copia editable abierta. Guarda los cambios en Word y cierra el documento antes del envío.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Carta de nombramientos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnRegenerarCartaNombramiento_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var filas = (TablaS2.ItemsSource as IEnumerable<RecomendacionS2UI>)?.ToList()
                            ?? new List<RecomendacionS2UI>();
                string ruta = PrepararCartaNombramientos(filas, true);
                TxtEstadoCarta.Text = "Carta regenerada desde la plantilla. Se ha creado una copia nueva; las ediciones anteriores no se transfieren.";
                Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Carta de nombramientos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnFormato_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string comando)
            {
                try { WbEditorEnvio.InvokeScript("eval", $"document.execCommand('{comando}', false, null);"); } catch { }
            }
        }

        private void CmbTamano_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cmb && cmb.SelectedItem is ComboBoxItem item && cmb.IsLoaded)
            {
                try { WbEditorEnvio.InvokeScript("eval", $"document.execCommand('fontSize', false, '{item.Tag}');"); } catch { }
            }
        }

        private async void BtnEnviarS2_Click(object sender, RoutedEventArgs e)
        {
            string mesRef = _mesS303.ToString("yyyy-MM");
            string identificadorVisita = $"S-303_{_congreCodigo}_{mesRef}";

            // Verificamos que el S-303 se haya generado
            var docEspecifico = DatabaseHelper.ObtenerDocumentos("S-303").FirstOrDefault(d => d.NombreArchivo.Contains(identificadorVisita));

            if (docEspecifico == null || !File.Exists(docEspecifico.RutaLocal))
            {
                MessageBox.Show("No se puede enviar el correo porque aún no has creado la plantilla del S-303 para este mes.", "Falta S-303", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var congre = DatabaseHelper.ObtenerCongregaciones().FirstOrDefault(c => c.Codigo == _congreCodigo);
            string emailDestino = congre?.Email ?? "";

            if (string.IsNullOrWhiteSpace(emailDestino))
            {
                MessageBox.Show("La congregación no tiene una dirección de correo electrónico configurada. Añádela primero desde el apartado de Edición de Congregaciones.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show("Debes ir a 'Configuración' y vincular tu cuenta de correo primero.", "Perfil no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string asunto = TxtAsuntoEnvio.Text;
            string cuerpoHtml = "";
            try { cuerpoHtml = WbEditorEnvio.InvokeScript("eval", "document.body.innerHTML").ToString(); } catch { }
            string rutaS303 = docEspecifico.RutaLocal;
            bool copiaSrv = ChkCopiaSRV.IsChecked == true;

            // Extraemos las recomendaciones (S-2) con la decisión Sí/No elegida.
            var recomendacionesS2 = (TablaS2.ItemsSource as IEnumerable<RecomendacionS2UI>)?.ToList()
                                    ?? new List<RecomendacionS2UI>();
            var recomendacionesAAprobar = recomendacionesS2.Where(r => r.Aprobar).Select(r => r.Original).ToList();
            var recomendacionesACancelar = recomendacionesS2.Where(r => !r.Aprobar).Select(r => r.Original).ToList();

            if (recomendacionesS2.Count > 0)
            {
                var confirmacion = MessageBox.Show(
                    $"Se van a procesar {recomendacionesS2.Count} recomendaciones de {_congreNombre}:\n\n" +
                    $"  • APROBAR: {recomendacionesAAprobar.Count}\n" +
                    $"  • CANCELAR: {recomendacionesACancelar.Count}\n\n" +
                    "Las marcadas como «No» se CANCELARÁN en JW Hub. Estos cambios pueden tener consecuencias reales.\n\n" +
                    "¿Confirmas las decisiones y el envío posterior del correo S-303?",
                    "Confirmar recomendaciones y bajas", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirmacion != MessageBoxResult.Yes) return;
            }
            else if (MessageBox.Show(
                "No hay recomendaciones pendientes para esta congregación. ¿Enviar solo el S-303?",
                "Confirmar envío", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            // Si hay aprobaciones, el PDF debe existir ANTES de cambiar el estado
            // de ninguna recomendación en Hub. Si falla Word/LibreOffice, no se
            // altera Hub ni se envía una carta incompleta.
            string rutaCartaPdf = null;
            if (recomendacionesAAprobar.Count > 0)
            {
                try
                {
                    string rutaDocx = PrepararCartaNombramientos(recomendacionesS2, false);
                    rutaCartaPdf = ConvertirCartaNombramientosAPdf(rutaDocx);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("No se pudo preparar la carta de nombramientos en PDF. " +
                                    "No se ha modificado JW Hub ni enviado el correo.\n\n" + ex.Message,
                                    "Carta de nombramientos", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            if (PanelCargando != null) PanelCargando.Visibility = Visibility.Visible;
            var botonEnvio = sender as FrameworkElement;
            if (botonEnvio != null) botonEnvio.IsEnabled = false;

            try
            {
                // Procesamos primero Hub y luego el correo. Cada operación confirmada
                // se elimina de la bandeja local: una interrupción no debe provocar
                // que se repitan automáticamente aprobaciones ya ejecutadas.
                var decisiones = recomendacionesS2.ToList();
                await Task.Run(() =>
                {
                    foreach (var fila in decisiones)
                    {
                        if (fila.Aprobar)
                        {
                            SincronizadorJWHub.AprobarRecomendacionHubSinCorreo(
                                folderPath, fila.Original, DateTime.Today);
                        }
                        else
                        {
                            SincronizadorJWHub.CancelarRecomendacionHub(
                                folderPath, fila.Original);
                        }

                        DatabaseHelper.EliminarRecomendacionProcesada(fila.Original);
                    }
                });

                await Task.Run(() =>
                {
                    // 1. AUTOMATIZACIÓN DE SELENIUM PARA EL ENVÍO DEL S-303
                    var service = FirefoxDriverService.CreateDefaultService();
                    service.HideCommandPromptWindow = true; // Que no aparezca la ventana negra de CMD

                    var options = new FirefoxOptions();
                    options.AddArgument("-profile");
                    options.AddArgument(folderPath);
                    options.AddArgument("-no-remote");

                    IWebDriver driver = new FirefoxDriver(service, options);

                    try
                    {
                        // Vamos al correo
                        driver.Navigate().GoToUrl("https://mail.jwpub.org/owa/#path=/mail");
                        WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

                        // Clic en "Nuevo"
                        IWebElement btnNuevo = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[contains(@title, 'Escribir un mensaje nuevo') or .//span[text()='Nuevo']]"));
                            foreach (var btn in botones) { if (btn.Displayed && btn.Enabled) return btn; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnNuevo);

                        // Campo "Para" (Congregación)
                        IWebElement inputCorreo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@aria-label='Para' and @role='textbox']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        System.Threading.Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputCorreo);

                        inputCorreo.SendKeys(emailDestino);
                        System.Threading.Thread.Sleep(500);
                        inputCorreo.SendKeys(Keys.Return);
                        System.Threading.Thread.Sleep(200);

                        // Si hay que enviar copia a SRV
                        if (copiaSrv)
                        {
                            string emailSrv = DatabaseHelper.ObtenerConfiguracion("EmailSRV", "srv.es@jwpub.org"); // Parametrizable
                            inputCorreo.SendKeys(emailSrv);
                            System.Threading.Thread.Sleep(500);
                            inputCorreo.SendKeys(Keys.Return);
                            System.Threading.Thread.Sleep(200);
                        }
                        inputCorreo.SendKeys(Keys.Tab); // Salir del campo Para

                        // Asunto
                        IWebElement inputAsunto = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[contains(@aria-label, 'Asunto') or @placeholder='Agregar un asunto']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputAsunto);
                        inputAsunto.SendKeys(asunto);

                        // Adjuntar S-303
                        IWebElement inputArchivo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@type='file']"));
                            return inputs.Count > 0 ? inputs[0] : null;
                        });
                        inputArchivo.SendKeys(rutaS303);
                        System.Threading.Thread.Sleep(1000);

                        // Esperar a que la subida del PDF termine (hasta 90 segundos si el internet va lento)
                        WebDriverWait waitSubida = new WebDriverWait(driver, TimeSpan.FromSeconds(90));
                        string nombreCortoS303 = Path.GetFileNameWithoutExtension(rutaS303);
                        if (nombreCortoS303.Length > 15) nombreCortoS303 = nombreCortoS303.Substring(0, 15);
                        try { waitSubida.Until(d => d.PageSource.Contains(nombreCortoS303)); } catch { }

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

                        // Carta: SOLO se adjunta si hubo al menos una aprobación.
                        if (!string.IsNullOrWhiteSpace(rutaCartaPdf))
                        {
                            // El input original puede haberse recreado tras subir el S-303.
                            var inputCarta = esperaArchivoParaCarta(driver, wait);
                            inputCarta.SendKeys(rutaCartaPdf);
                            WebDriverWait esperaCarta = new WebDriverWait(driver, TimeSpan.FromSeconds(90));
                            string nombreCarta = Path.GetFileNameWithoutExtension(rutaCartaPdf);
                            if (nombreCarta.Length > 15) nombreCarta = nombreCarta.Substring(0, 15);
                            esperaCarta.Until(d => d.PageSource.Contains(nombreCarta));
                            esperaCarta.Until(d => !d.FindElements(By.XPath(
                                "//div[contains(@class, 'spinnerAnimation')]")).Any(x => x.Displayed));
                        }

                        // Cuerpo del mensaje (Ya está en HTML gracias al editor)
                        IWebElement inputCuerpo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//div[@role='textbox' and @aria-label='Cuerpo del mensaje']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });

                        ((IJavaScriptExecutor)driver).ExecuteScript(
                            "arguments[0].focus(); document.execCommand('insertHTML', false, arguments[1]);",
                            inputCuerpo, cuerpoHtml);

                        System.Threading.Thread.Sleep(1000);

                        // Clic en Enviar
                        IWebElement btnEnviar = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[@aria-label='Enviar' or @title='Enviar']"));
                            foreach (var b in botones) { if (b.Displayed && b.Enabled) return b; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);

                        // Dejamos 4 segundos para asegurarnos de que da tiempo a que la página procese el envío
                        System.Threading.Thread.Sleep(4000);
                    }
                    finally
                    {
                        if (driver != null) driver.Quit();
                    }

                });

                RefrescarTablaRecomendacionesS2();
                MessageBox.Show("El S-303 se ha enviado. Las aprobaciones y cancelaciones de JW Hub se han procesado. " +
                                (rutaCartaPdf != null ? "Se adjuntó la carta de nombramientos en PDF." : "No se adjuntó carta porque no hubo aprobaciones."),
                                "Envío completado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                RefrescarTablaRecomendacionesS2();
                MessageBox.Show("No se ha completado el proceso. Es posible que algunas decisiones en JW Hub " +
                                "ya estén aplicadas, pero que el correo todavía NO se haya enviado. " +
                                "Consulta la bandeja de recomendaciones y la carpeta de enviados antes de reintentar.\n\n" +
                                ex.Message, "Proceso interrumpido", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (botonEnvio != null) botonEnvio.IsEnabled = true;
                if (PanelCargando != null) PanelCargando.Visibility = Visibility.Collapsed;
            }
        }

        public class NominatimResult { public string lat { get; set; } public string lon { get; set; } }
        public class MesSelectorVisita { public DateTime FechaBase { get; set; } public string NombreVisual { get; set; } }

        public class RecomendacionS2UI : INotifyPropertyChanged
        {
            public RecomendacionHub Original { get; set; }
            public string Hermano => Original?.Hermano;
            public string Tipo => Original?.Tipo;
            public string RecomendadoComo => Original?.RecomendadoComo;

            // Decisión elegida en el select: «Sí» = aprobar en JW Hub;
            // «No» = cancelar en JW Hub al enviar.
            private string _decision = "No";
            public string Decision
            {
                get { return _decision; }
                set
                {
                    _decision = value ?? "No";
                    OnPropertyChanged("Decision");
                    OnPropertyChanged("Aprobar");
                }
            }

            public bool Aprobar =>
                string.Equals(_decision, "Sí", StringComparison.OrdinalIgnoreCase);

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class FilaVisitaPublisher
        {
            public int PersonaId { get; set; }
            public int PersonaRaizId { get; set; }
            public string Nombre { get; set; }
            public string NombreComun { get; set; }
            public string NombreVisual { get; set; }
            public string AlertaTooltip { get; set; }
            public string NombreJWHub { get; set; }
            public int Grupo { get; set; }
            public string GrupoPredicacion => Grupo > 0 ? $"GRUPO {Grupo}" : "SIN ASIGNAR";
            public string Nombramiento { get; set; }
            public string Servicio { get; set; }
            public string Edad { get; set; }
            public string AniosBautizado { get; set; }
            public string Sexo { get; set; }
            public DateTime FechaNacimientoReal { get; set; }
            public string Foto { get; set; }

            public string ActividadM1 { get; set; }
            public string ActividadM2 { get; set; }
            public string ActividadM3 { get; set; }
            public string ActividadM4 { get; set; }
            public string ActividadM5 { get; set; }
            public string ActividadM6 { get; set; }

            public string NotasM1 { get; set; }
            public string NotasM2 { get; set; }
            public string NotasM3 { get; set; }
            public string NotasM4 { get; set; }
            public string NotasM5 { get; set; }
            public string NotasM6 { get; set; }

            public Brush ColorM1 { get; set; }
            public Brush ColorM2 { get; set; }
            public Brush ColorM3 { get; set; }
            public Brush ColorM4 { get; set; }
            public Brush ColorM5 { get; set; }
            public Brush ColorM6 { get; set; }

            // Horas acumuladas del año de servicio hasta cada mes
            // (solo precursores con requisito anual).
            public string AcumuladoM1 { get; set; } = "";
            public string AcumuladoM2 { get; set; } = "";
            public string AcumuladoM3 { get; set; } = "";
            public string AcumuladoM4 { get; set; } = "";
            public string AcumuladoM5 { get; set; } = "";
            public string AcumuladoM6 { get; set; } = "";

            public Brush ColorAcumuladoM1 { get; set; } = Brushes.Gray;
            public Brush ColorAcumuladoM2 { get; set; } = Brushes.Gray;
            public Brush ColorAcumuladoM3 { get; set; } = Brushes.Gray;
            public Brush ColorAcumuladoM4 { get; set; } = Brushes.Gray;
            public Brush ColorAcumuladoM5 { get; set; } = Brushes.Gray;
            public Brush ColorAcumuladoM6 { get; set; } = Brushes.Gray;

            public string CursosUltimoMes { get; set; }
            public string TerritoriosTrabajados { get; set; }
            public bool EsPrecursor { get; set; }

            // Sin actividad informada en los últimos 6 meses.
            public bool EsInactivo { get; set; }

            // Nombre del apartado en el que se agrupa la fila dentro de la
            // tabla: los inactivos van bajo su propia cabecera al final.
            public string SeccionTabla =>
                EsInactivo ? "PUBLICADORES INACTIVOS" : "Activos";
        }

        public class FilaTablaTerritorio
        {
            public string Nombre { get; set; }
            public string UltimaVez { get; set; }

            // Estados calculados con exactamente las mismas reglas que las
            // tarjetas-resumen. Se usan para filtrar el listado al pulsarlas.
            public bool TrabajadoUltimos6Meses { get; set; }
            public bool TrabajadoAnioServicio { get; set; }
            public bool TrabajandoseEnFechaCorte { get; set; }
            public bool Caducado { get; set; }
            public bool SinTrabajar1Ano { get; set; }
            public bool SinTrabajar2Anos { get; set; }

            public List<CeldaVuelta>
                RegistrosAnioServicio
            { get; set; } =
                new List<CeldaVuelta>();
        }

        public class CeldaVuelta
        {
            public int RegistroId { get; set; }
            public string NombreS13 { get; set; }
            public string NombreS21 { get; set; }
            public string Fechas { get; set; }
            public List<OpcionPersonaTerritorio> ListaPersonasS21 { get; set; } =
                new List<OpcionPersonaTerritorio>();
            public Visibility EsVisible { get; set; } = Visibility.Collapsed;
        }

        public class OpcionPersonaTerritorio
        {
            public int PersonaId { get; set; }
            public string NombreS21 { get; set; }
            public string NombreComun { get; set; }
            public string NombreJWHub { get; set; }
            public int Coincidencia { get; set; }

            // Para mostrar una persona en Territorios usamos este orden:
            // 1) Nombre común, 2) Nombre JW Hub, 3) Nombre completo/S-21.
            public string NombreVisible
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(NombreComun))
                        return NombreComun.Trim();

                    if (!string.IsNullOrWhiteSpace(NombreJWHub))
                        return NombreJWHub.Trim();

                    return NombreS21?.Trim() ?? "";
                }
            }

            public string TextoMostrar
            {
                get
                {
                    if (NombreS21 == "-- Sin Asignar --")
                        return NombreS21;

                    string nombre = NombreVisible;

                    return Coincidencia >= 0
                        ? $"{nombre} — {Coincidencia}%"
                        : nombre;
                }
            }

            public override string ToString() => TextoMostrar;
        }

        public class EstadisticasGrupo
        {
            public int NumeroGrupo { get; set; }
            public string NombreGrupo => $"Grupo {NumeroGrupo}";
            public int NumPublicadores { get; set; }
            public int NumPrecursores { get; set; }
            public int NumAncianos { get; set; }
            public int NumSM { get; set; }
            public double AnchoPublicadores => NumPublicadores * 15;
            public double AnchoPrecursores => NumPrecursores * 15;
            public double AnchoAncianos => NumAncianos * 15;
            public double AnchoSM => NumSM * 15;
        }

        public class EstadisticasInformacion
        {
            public int TotalPersonas { get; set; }
            public int TotalHombres { get; set; }
            public int TotalMujeres { get; set; }
            public int NoBautizados { get; set; }
            public int Ancianos { get; set; }
            public int SiervosMinisteriales { get; set; }
            public int PrecursoresRegulares { get; set; }
            public int PrecursoresEspeciales { get; set; }
            public int AuxiliaresMesEscogido { get; set; }
            public int Auxiliares6Meses { get; set; }
            public int BautizadosRecientes { get; set; }
            public int Irregulares { get; set; }
            public string MediaEdad { get; set; }
            public int TotalCursos { get; set; }
            public string MediaCursos { get; set; }
        }

        public class CategoriaNotas
        {
            public string Cabecera { get; set; }
            public List<NotaVisita> Preguntas { get; set; }
        }

        public class DiaProgramaUI
        {
            public string Dia { get; set; }
            public List<BotonActividadUI> Actividades { get; set; } =
                new List<BotonActividadUI>();

            public Visibility VerSinActividades =>
                Actividades == null || Actividades.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        public class FilaPrograma
        {
            public string Turno { get; set; }
            public string Persona { get; set; }
            public CeldaPrograma Miercoles { get; set; }
            public CeldaPrograma Jueves { get; set; }
            public CeldaPrograma Viernes { get; set; }
            public CeldaPrograma Sabado { get; set; }
            public CeldaPrograma Domingo { get; set; }
        }

        public class DocumentoS303UI
        {
            public string NombreVisual { get; set; }
            public string TextoBoton { get; set; }
            public bool EsPlantilla { get; set; }

            // ESTO ES LO QUE LO OCULTA SI ES UNA PLANTILLA
            public Visibility VerBotonBorrar => EsPlantilla ? Visibility.Collapsed : Visibility.Visible;

            public ArchivoDocumento DocumentoOriginal { get; set; }
        }

        public class CeldaPrograma
        {
            public string Dia { get; set; }
            public string Turno { get; set; }
            public string Responsable { get; set; }
            public Visibility VerBotonAnadir { get; set; }
            public List<BotonActividadUI> ListaActividades { get; set; }
        }

        public class BotonActividadUI
        {
            public string Hora { get; set; }
            public string TipoActividad { get; set; }
            public string Acompanante { get; set; }
            public string Ubicacion { get; set; }
            public string Notas { get; set; }

            // Se mantienen estas propiedades por compatibilidad con código
            // histórico del programa de la visita.
            public string Texto { get; set; }
            public string Dia { get; set; }
            public string Turno { get; set; }
            public string Responsable { get; set; }
            public DatabaseHelper.ActividadPrograma ActividadDB { get; set; }

            /// <summary>La insignia solo aparece si hay persona concreta.</summary>
            public System.Windows.Visibility VerResponsable =>
                string.IsNullOrWhiteSpace(Responsable)
                    ? System.Windows.Visibility.Collapsed
                    : System.Windows.Visibility.Visible;

            /// <summary>El botón de Google Maps solo aparece si hay ubicación.</summary>
            public System.Windows.Visibility VerMapa =>
                (ActividadDB != null
                 && (ActividadDB.Latitud != 0
                     || ActividadDB.Longitud != 0
                     || !string.IsNullOrWhiteSpace(
                         ActividadDB.Direccion)))
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
        }

        public class FilaContabilidad : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            private string _gastos;
            private string _mantenimiento;
            private string _obraMundial;
            private string _obraResolucion;
            private string _saldoMaximo;
            private string _saldo;
            private int _publicadoresActivos;

            public string MesReferencia { get; set; }
            public string NombreMesVisual { get; set; }

            public string Gastos
            {
                get => _gastos;
                set
                {
                    if (_gastos == value) return;
                    _gastos = value;
                    OnPropertyChanged(nameof(Gastos));
                    OnPropertyChanged(nameof(Promedio));
                }
            }

            public string Mantenimiento
            {
                get => _mantenimiento;
                set
                {
                    if (_mantenimiento == value) return;
                    _mantenimiento = value;
                    OnPropertyChanged(nameof(Mantenimiento));
                }
            }

            public string ObraMundial
            {
                get => _obraMundial;
                set
                {
                    if (_obraMundial == value) return;
                    _obraMundial = value;
                    OnPropertyChanged(nameof(ObraMundial));
                    OnPropertyChanged(nameof(Promedio));
                }
            }

            public string ObraResolucion
            {
                get => _obraResolucion;
                set
                {
                    if (_obraResolucion == value) return;
                    _obraResolucion = value;
                    OnPropertyChanged(nameof(ObraResolucion));
                }
            }

            public string SaldoMaximo
            {
                get => _saldoMaximo;
                set
                {
                    if (_saldoMaximo == value) return;
                    _saldoMaximo = value;
                    OnPropertyChanged(nameof(SaldoMaximo));
                    OnPropertyChanged(nameof(SaldoSuperaMaximo));
                }
            }

            public string Saldo
            {
                get => _saldo;
                set
                {
                    if (_saldo == value) return;
                    _saldo = value;
                    OnPropertyChanged(nameof(Saldo));
                    OnPropertyChanged(nameof(SaldoSuperaMaximo));
                }
            }

            public int PublicadoresActivos
            {
                get => _publicadoresActivos;
                set
                {
                    if (_publicadoresActivos == value) return;
                    _publicadoresActivos = value;
                    OnPropertyChanged(nameof(PublicadoresActivos));
                    OnPropertyChanged(nameof(Promedio));
                }
            }

            public bool SaldoSuperaMaximo =>
                ParsearMonedaHelper(Saldo)
                >
                ParsearMonedaHelper(SaldoMaximo);

            public string Promedio
            {
                get
                {
                    // El promedio solicitado se calcula únicamente con
                    // Gastos + Obra mundial.
                    double suma =
                        ParsearMonedaHelper(Gastos)
                        +
                        ParsearMonedaHelper(ObraMundial);

                    if (PublicadoresActivos <= 0)
                    {
                        return "0,00 €";
                    }

                    return
                        (suma / PublicadoresActivos)
                            .ToString(
                                "N2",
                                CultureInfo.GetCultureInfo("es-ES"))
                        + " €";
                }
            }

            public void NormalizarMonedas()
            {
                Gastos = FormatearMonedaHelper(ParsearMonedaHelper(Gastos));
                Mantenimiento = FormatearMonedaHelper(ParsearMonedaHelper(Mantenimiento));
                ObraMundial = FormatearMonedaHelper(ParsearMonedaHelper(ObraMundial));
                ObraResolucion = FormatearMonedaHelper(ParsearMonedaHelper(ObraResolucion));
                SaldoMaximo = FormatearMonedaHelper(ParsearMonedaHelper(SaldoMaximo));
                Saldo = FormatearMonedaHelper(ParsearMonedaHelper(Saldo));
            }

            public void ForzarActualizacionPromedio()
            {
                OnPropertyChanged(nameof(Promedio));
                OnPropertyChanged(nameof(SaldoSuperaMaximo));
            }

            private void OnPropertyChanged(string nombre)
            {
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nombre));
            }

            private static string FormatearMonedaHelper(double valor)
            {
                return
                    valor.ToString(
                        "N2",
                        CultureInfo.GetCultureInfo("es-ES"))
                    + " €";
            }

            private static double ParsearMonedaHelper(string texto)
            {
                if (string.IsNullOrWhiteSpace(texto))
                {
                    return 0;
                }

                string limpio =
                    texto
                        .Replace("€", "")
                        .Replace("\u00A0", " ")
                        .Trim();

                if (double.TryParse(
                        limpio,
                        NumberStyles.Number,
                        CultureInfo.GetCultureInfo("es-ES"),
                        out double valorEs))
                {
                    return valorEs;
                }

                if (double.TryParse(
                        limpio.Replace(",", "."),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double valorInv))
                {
                    return valorInv;
                }

                return 0;
            }
        }
    }
}