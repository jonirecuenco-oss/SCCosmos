using iText.Forms;
using iText.Kernel.Pdf;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
        private List<FilaTablaTerritorio> _territoriosGlobal = new List<FilaTablaTerritorio>();
        private int _paginaActual = 1;
        private const int _itemsPorPagina = 20;

        // Variables de estado independientes para cada módulo
        private DateTime _mesMinisterio;
        private DateTime _mesPrograma;
        private DateTime _mesNotas;
        private DateTime _mesTerritorios;
        private DateTime _mesContabilidad;
        private DateTime _mesS303;

        private bool _mapaCargado = false;
        private string _htmlMapaCache = "";

        public List<CategoriaNotas> MisNotasAgrupadas { get; set; }

        public VisitasPage()
        {
            InitializeComponent();
            this.Loaded += Page_Loaded;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CodigoPendiente)) return;

            if (PanelCargando != null) PanelCargando.Visibility = Visibility.Visible;
            await Task.Delay(50);

            _congreCodigo = CodigoPendiente;
            _congreNombre = NombrePendiente;
            TxtTitulo.Text = $"DATOS DE LA CONGREGACIÓN: {_congreNombre.ToUpper()}";

            InicializarMeses();
            CargarS303yS2();
            AjustarVisibilidadColumnas(this.ActualWidth);

            if (PanelCargando != null) PanelCargando.Visibility = Visibility.Collapsed;
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AjustarVisibilidadColumnas(e.NewSize.Width);
        }

        private void AjustarVisibilidadColumnas(double anchoPantalla)
        {
            if (TablaVisitas == null || TablaVisitas.Columns.Count == 0) return;

            foreach (var col in TablaVisitas.Columns)
            {
                string header = col.Header?.ToString();
                if (string.IsNullOrEmpty(header)) continue;

                if (header == "Nombre JW Hub") col.Visibility = anchoPantalla < 1250 ? Visibility.Collapsed : Visibility.Visible;
                else if (header == "Sexo" || header == "Edad" || header == "Baut.") col.Visibility = anchoPantalla < 1100 ? Visibility.Collapsed : Visibility.Visible;
                else if (header == "Terrs." || header == "CBs") col.Visibility = anchoPantalla < 950 ? Visibility.Collapsed : Visibility.Visible;
            }
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

        private DateTime CalcularMaximoMesActividad()
        {
            DateTime maxFecha = DateTime.MinValue;
            string[] nombresMesesJson = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
            var listadoHermanos = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo);

            foreach (var h in listadoHermanos)
            {
                var historico = DatabaseHelper.ObtenerActividadPersona(h.Id);
                foreach (var act in historico)
                {
                    bool horasMayorQueCero = false;
                    if (double.TryParse(act.Horas?.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double hMes))
                    {
                        if (hMes > 0) horasMayorQueCero = true;
                    }

                    if (act.Participacion || horasMayorQueCero)
                    {
                        int mIndex = Array.IndexOf(nombresMesesJson, act.Mes?.ToLower());
                        if (mIndex >= 0 && int.TryParse(act.AnioServicio, out int anioServ))
                        {
                            int y = (mIndex >= 8) ? anioServ - 1 : anioServ;
                            DateTime d = new DateTime(y, mIndex + 1, 1);
                            if (d > maxFecha) maxFecha = d;
                        }
                    }
                }
            }
            return maxFecha;
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

                await ProcesarColumnasYDatosDinamicos(mesesARenderizar, listadoHermanosHistorico);
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

                // Para disponer de los nombres S-21 usamos la instantánea
                // más cercana posible dentro del año seleccionado.
                DateTime fechaPersonas =
                    DateTime.Today < inicioAnioServicio
                        ? inicioAnioServicio
                        : (
                            DateTime.Today > finAnioServicio
                                ? finAnioServicio
                                : DateTime.Today
                          );

                var listadoHermanosHistorico =
                    DatabaseHelper
                        .ObtenerPersonasHistoricasPorMes(
                            _congreCodigo,
                            fechaPersonas.ToString(
                                "yyyy-MM"));

                await CargarTerritorios(
                    anioServicio,
                    inicioAnioServicio,
                    finAnioServicio,
                    listadoHermanosHistorico);
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

        // --- CONTABILIDAD ---
        private void ActualizarUI_MesContabilidad()
        {
            if (TxtMesContabilidad != null) TxtMesContabilidad.Text = FormatearMes(_mesContabilidad);
            if (!_cargandoDatos) CargarContabilidadList(_mesContabilidad);
        }
        private void BtnMesAntContabilidad_Click(object sender, RoutedEventArgs e) { _mesContabilidad = _mesContabilidad.AddMonths(-1); ActualizarUI_MesContabilidad(); }
        private void BtnMesSigContabilidad_Click(object sender, RoutedEventArgs e) { _mesContabilidad = _mesContabilidad.AddMonths(1); ActualizarUI_MesContabilidad(); }

        // --- S-303 ---
        private void ActualizarUI_MesS303()
        {
            if (!_cargandoDatos) CargarS303yS2();
        }

        // ==========================================
        // CREADORES DE MÓDULOS (PROGRAMA Y NOTAS)
        // ==========================================
        private void CargarPrograma(DateTime mesReferencia)
        {
            string mesRefStr = mesReferencia.ToString("yyyy-MM");

            string[] turnos = { "1. MAÑANA", "2. MEDIODÍA", "3. TARDE", "4. NOCHE" };
            var filasPrograma = new List<FilaPrograma>();

            foreach (var turno in turnos)
            {
                if (turno == "2. MEDIODÍA") filasPrograma.Add(CrearFilaPrograma(mesRefStr, turno, "Comida"));
                else if (turno == "4. NOCHE")
                {
                    filasPrograma.Add(CrearFilaPrograma(mesRefStr, turno, "Cenar"));
                    filasPrograma.Add(CrearFilaPrograma(mesRefStr, turno, "Dormir"));
                }
                else
                {
                    filasPrograma.Add(CrearFilaPrograma(mesRefStr, turno, "Joni"));
                    filasPrograma.Add(CrearFilaPrograma(mesRefStr, turno, "Rebe"));
                }
            }

            var collectionView = CollectionViewSource.GetDefaultView(filasPrograma);
            collectionView.GroupDescriptions.Clear();
            collectionView.GroupDescriptions.Add(new PropertyGroupDescription("Turno"));
            TablaPrograma.ItemsSource = collectionView;
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
                var notasPlanas = DatabaseHelper.ObtenerNotasVisita(idVisita);
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
            if (sender is Button btn && btn.Tag is CeldaPrograma celda)
            {
                var ventana =
                    new ProgramarActividadWindow(
                        celda.Responsable,
                        celda.Turno,
                        celda.Dia,
                        _congreCodigo,
                        _mesPrograma.ToString(
                            "yyyy-MM"));

                ventana.Owner =
                    Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    ActualizarUI_MesPrograma();
                }
            }
        }

        private void BtnEliminarActividad_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button btn
                &&
                btn.Tag is BotonActividadUI ui)
            {
                if (MessageBox.Show(
                        $"¿Eliminar actividad de {ui.Responsable} a las {ui.ActividadDB.Hora}?",
                        "Confirmar Eliminación",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning)
                    != MessageBoxResult.Yes)
                {
                    return;
                }

                if (ui.ActividadDB.Id > 0)
                {
                    DatabaseHelper
                        .EliminarActividadPrograma(
                            ui.ActividadDB.Id);
                }
                else
                {
                    // Compatibilidad con registros/código antiguo.
                    using (
                        var connection =
                            new Microsoft.Data.Sqlite
                                .SqliteConnection(
                                    $"Data Source={DatabaseHelper.RutaDirectorioDatos}\\sccosmos.db"))
                    {
                        connection.Open();

                        var cmd =
                            connection.CreateCommand();

                        cmd.CommandText =
                            "DELETE FROM ProgramaVisitaV2 " +
                            "WHERE CongreCodigo=@c " +
                            "AND MesReferencia=@m " +
                            "AND DiaSemana=@d " +
                            "AND Turno=@t " +
                            "AND Responsable=@r " +
                            "AND Hora=@h";

                        cmd.Parameters.AddWithValue(
                            "@c",
                            _congreCodigo);

                        cmd.Parameters.AddWithValue(
                            "@m",
                            _mesPrograma.ToString(
                                "yyyy-MM"));

                        cmd.Parameters.AddWithValue(
                            "@d",
                            ui.Dia);

                        cmd.Parameters.AddWithValue(
                            "@t",
                            ui.Turno);

                        cmd.Parameters.AddWithValue(
                            "@r",
                            ui.Responsable);

                        cmd.Parameters.AddWithValue(
                            "@h",
                            ui.ActividadDB.Hora);

                        cmd.ExecuteNonQuery();
                    }
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
                btn.Tag is BotonActividadUI ui)
            {
                var ventana =
                    new ProgramarActividadWindow(
                        ui.Responsable,
                        ui.Turno,
                        ui.Dia,
                        _congreCodigo,
                        _mesPrograma.ToString(
                            "yyyy-MM"),
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

                var estiloTextoColorDinamico = new Style(typeof(TextBlock), estiloIzquierda);
                estiloTextoColorDinamico.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding($"ColorM{i + 1}")));

                TablaVisitas.Columns.Add(new DataGridTextColumn
                {
                    Header = $"{shortMonths[meses[i].Month - 1]} {meses[i].ToString("yy")}",
                    Binding = new Binding($"ActividadM{i + 1}"),
                    Width = new DataGridLength(6, DataGridLengthUnitType.Star),
                    ElementStyle = estiloTextoColorDinamico,
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
                Width = new DataGridLength(7, DataGridLengthUnitType.Star),
                CellTemplate = (DataTemplate)this.FindResource("BotonEditarTemplate")
            };
            TablaVisitas.Columns.Add(colAcciones);

            AjustarVisibilidadColumnas(this.ActualWidth);

            var territoriosBD = DatabaseHelper.ObtenerTerritoriosPorCongregacion(_congreCodigo);
            var filasGrid = new List<FilaVisitaPublisher>();

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

            int contadorRespiro = 0;

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

                contadorRespiro++;
                if (contadorRespiro % 10 == 0) await Task.Delay(1);

                string nombreLimpio = !string.IsNullOrWhiteSpace(h.Nombre) ? h.Nombre.Trim() : "";
                string nombreComunLimpio = !string.IsNullOrWhiteSpace(h.NombreComun) ? h.NombreComun.Trim() : nombreLimpio;
                string jwHubLimpio = !string.IsNullOrWhiteSpace(h.NombreJWHub) ? h.NombreJWHub.Trim() : "";
                string nombramientoLimpio = !string.IsNullOrWhiteSpace(h.Nombramiento) ? h.Nombramiento.Trim() : "";
                string servicioLimpio = !string.IsNullOrWhiteSpace(h.Servicio) ? h.Servicio.Trim() : "";
                string sexoLimpio = !string.IsNullOrWhiteSpace(h.Sexo) ? h.Sexo.Trim() : "-";

                bool esPrec = !string.IsNullOrWhiteSpace(servicioLimpio) && (servicioLimpio.ToLower().Contains("precursor") || servicioLimpio.ToLower().Contains("misionero"));

                long idParaActividad = h.PersonaRaizId > 0 ? h.PersonaRaizId : h.Id;
                var informesHistoricos = DatabaseHelper.ObtenerActividadPersona((int)idParaActividad);
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

                if (h.Grupo > 0)
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

                if (requisitoAnual == 0 && esPrec)
                {
                    string srv = servicioLimpio.ToLower();
                    if (srv.Contains("precursor regular")) requisitoAnual = 600;
                    else if (srv.Contains("especial") || srv.Contains("misionero"))
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
                        DatabaseHelper.ActualizarRequisitoSilencioso(h.Id, requisitoAnual);
                    }
                }

                string nombreMostrar = nombreComunLimpio;
                if (esPrec) nombreMostrar = "   " + nombreMostrar;

                string tooltipAlerta = null;

                if (requisitoAnual > 0)
                {
                    double metaProrrateada = (requisitoAnual / 12.0) * mesesTranscurridos;
                    double horasTotales = 0;

                    foreach (var act in informesHistoricos.Where(a => a.AnioServicio == anioServicioGrid))
                    {
                        int numMesAct = Array.IndexOf(nombresMesesJson, act.Mes.ToLower()) + 1;
                        int ordenMesAct = (numMesAct >= 9) ? numMesAct - 8 : numMesAct + 4;
                        if (ordenMesAct <= mesesTranscurridos)
                        {
                            string hrClean = act.Horas?.Replace(" ", "").Trim();
                            if (double.TryParse(hrClean?.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double hMes)) horasTotales += hMes;
                        }
                    }

                    if (horasTotales < metaProrrateada)
                    {
                        nombreMostrar += "  ⚠️";
                        double deficit = Math.Round(metaProrrateada - horasTotales, 1);
                        tooltipAlerta = $"ATENCIÓN: Atraso de Horas\n\nMeta prorrateada (hasta {mesReferenciaGrid.ToString("MMMM")}): {Math.Round(metaProrrateada, 1)}h\nHoras logradas: {horasTotales}h\n\nDéficit actual: -{deficit}h\nMeta anual asignada: {requisitoAnual}h";
                    }
                }

                var act1 = LocalizarActividadMes(informesHistoricos, meses[0]);
                var act2 = LocalizarActividadMes(informesHistoricos, meses[1]);
                var act3 = LocalizarActividadMes(informesHistoricos, meses[2]);
                var act4 = LocalizarActividadMes(informesHistoricos, meses[3]);
                var act5 = LocalizarActividadMes(informesHistoricos, meses[4]);
                var act6 = LocalizarActividadMes(informesHistoricos, meses[5]);

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
                    Edad = (fn.HasValue && fn.Value.Year > 1900 && edadNumerica > 0 && edadNumerica <= 120) ? edadNumerica.ToString("0.0") : "-",
                    AniosBautizado = (fb.HasValue && fb.Value.Year > 1900 && bautNumerica > 0 && bautNumerica <= 120) ? bautNumerica.ToString("0.0") : "-",
                    Sexo = sexoLimpio,
                    FechaNacimientoReal = fn ?? DateTime.MaxValue,
                    Foto = h.Foto,

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

            var gruposUnicos = _filasVisitasGlobal.Select(f => f.GrupoPredicacion).Distinct().OrderBy(g => g == "SIN ASIGNAR" ? 1 : 0).ThenBy(g => g).ToList();
            TabGrupos.SelectionChanged -= TabGrupos_SelectionChanged;
            TabGrupos.ItemsSource = gruposUnicos;
            TabGrupos.SelectionChanged += TabGrupos_SelectionChanged;

            if (gruposUnicos.Count > 0)
            {
                int idx = TabGrupos.SelectedIndex >= 0 ? TabGrupos.SelectedIndex : 0;
                if (idx >= gruposUnicos.Count) idx = 0;

                TabGrupos.SelectedIndex = idx;
                string grupoActivo = gruposUnicos[idx];
                var filtrados = _filasVisitasGlobal.Where(f => f.GrupoPredicacion == grupoActivo).ToList();
                TablaVisitas.ItemsSource = filtrados;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TablaVisitas.UpdateLayout();
                    foreach (var col in TablaVisitas.Columns)
                    {
                        var anchoActual = col.Width;
                        col.Width = 0; col.Width = anchoActual;
                    }
                }), DispatcherPriority.Loaded);
            }
            else TablaVisitas.ItemsSource = null;
        }

        private void CargarGruposPredicacion(List<Persona> listadoHermanos)
        {
            var estadisticas = listadoHermanos
                .Where(p => p.Grupo > 0)
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
            if (TabGrupos.SelectedItem is string grupoSeleccionado)
            {
                var filtrados = _filasVisitasGlobal.Where(f => f.GrupoPredicacion == grupoSeleccionado).ToList();
                TablaVisitas.ItemsSource = filtrados;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TablaVisitas.UpdateLayout();
                    foreach (var col in TablaVisitas.Columns)
                    {
                        var anchoActual = col.Width;
                        col.Width = 0; col.Width = anchoActual;
                    }
                }), DispatcherPriority.Loaded);
            }
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

            var grupos =
                listaPlana
                    .GroupBy(
                        t =>
                            t.Nombre)
                    .ToList();

            var nombresS21Disponibles =
                personasMes
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
                            p.Nombre)
                    .Where(
                        n =>
                            !string.IsNullOrWhiteSpace(
                                n))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        n =>
                            n)
                    .ToList();

            nombresS21Disponibles.Insert(
                0,
                "-- Sin Asignar --");

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


            // Esta estadística es móvil y NO debe limitarse al año de
            // servicio seleccionado. Por ejemplo, a 31/01/2026 debe poder
            // contar trabajos de agosto-diciembre de 2025 aunque parte de ese
            // intervalo pertenezca al año de servicio anterior.
            DateTime inicioUltimos6Meses =
                fechaCorte.AddMonths(-6).AddDays(1);


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

            int contadorRespiro =
                0;


            foreach (var grupo
                     in grupos)
            {
                contadorRespiro++;

                if (contadorRespiro % 10 == 0)
                {
                    await Task.Delay(1);
                }


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
                                            nombresS21Disponibles,
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


                    if (fComp.HasValue
                        &&
                        fComp.Value.Date >=
                        inicioAnioServicio.Date
                        &&
                        fComp.Value.Date <=
                        fechaCorte.Date)
                    {
                        trabajadoEnAnio =
                            true;
                    }


                    if ((fComp.HasValue
                         && fComp.Value.Date >= inicioUltimos6Meses.Date
                         && fComp.Value.Date <= fechaCorte.Date)
                        ||
                        (fUltimaCompJson.HasValue
                         && fUltimaCompJson.Value.Date >= inicioUltimos6Meses.Date
                         && fUltimaCompJson.Value.Date <= fechaCorte.Date))
                    {
                        trabajadoEn6Meses =
                            true;
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
                        sinTrabajar2Anos++;
                    }
                    else if (diasInactivo >=
                             365)
                    {
                        sinTrabajar1Ano++;
                    }
                }
                else
                {
                    sinTrabajar2Anos++;
                }
            }


            _territoriosGlobal =
                filasGrid
                    .OrderBy(
                        f =>
                            ObtenerNumeroTerritorio(f.Nombre))
                    .ThenBy(
                        f =>
                            f.Nombre,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            MostrarPaginaTerritorios(
                1);


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


        private void MostrarPaginaTerritorios(int pagina)
        {
            // Si no hay territorios para el año seleccionado, limpiamos la vista
            // en lugar de dejar datos/paginación de una carga anterior.
            if (_territoriosGlobal == null || !_territoriosGlobal.Any())
            {
                _paginaActual = 1;
                if (TablaTerritorios != null) TablaTerritorios.ItemsSource = null;
                if (PanelPaginacion != null) PanelPaginacion.Children.Clear();
                return;
            }

            int totalPaginas = (int)Math.Ceiling(_territoriosGlobal.Count / (double)_itemsPorPagina);
            if (pagina < 1) pagina = 1;
            if (pagina > totalPaginas) pagina = totalPaginas;

            _paginaActual = pagina;

            var datosPagina = _territoriosGlobal.Skip((pagina - 1) * _itemsPorPagina).Take(_itemsPorPagina).ToList();
            TablaTerritorios.ItemsSource = datosPagina;

            PanelPaginacion.Children.Clear();

            for (int i = 1; i <= totalPaginas; i++)
            {
                Button btn = new Button
                {
                    Content = i.ToString(),
                    Tag = i,
                    Style = (Style)this.FindResource("EstiloBotonPaginacion"),
                    Background = i == _paginaActual ? (SolidColorBrush)new BrushConverter().ConvertFrom("#0078D4") : (SolidColorBrush)new BrushConverter().ConvertFrom("#333333")
                };
                btn.Click += BtnPaginaTerritorio_Click;
                PanelPaginacion.Children.Add(btn);
            }
        }

        private void BtnPaginaTerritorio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int paginaTarget) MostrarPaginaTerritorios(paginaTarget);
        }

        private async void ExpanderMapa_Expanded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Expander expander)
            {
                return;
            }

            // WebBrowser es un HWND nativo y no participa en el z-order
            // normal de WPF. Si se hace visible durante el primer cálculo
            // de tamaño del Expander puede dibujarse temporalmente fuera de
            // su hueco. Lo mantenemos oculto hasta que ScrollViewer y
            // Expander hayan terminado su layout.
            MapaWebBrowser.Visibility =
                Visibility.Collapsed;

            expander.UpdateLayout();
            MainScrollViewer?.UpdateLayout();

            await Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.Loaded);

            await Task.Delay(120);

            expander.UpdateLayout();
            MainScrollViewer?.UpdateLayout();

            if (!expander.IsExpanded)
            {
                return;
            }

            MapaWebBrowser.Visibility =
                Visibility.Visible;

            MapaWebBrowser.UpdateLayout();

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

                            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabaseHelper.RutaDirectorioDatos}\\sccosmos.db"))
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

        private void ExpanderMapa_Collapsed(object sender, RoutedEventArgs e)
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

        private void BtnEditarPersona_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FilaVisitaPublisher fila) AbrirFormularioPersona(fila.PersonaId);
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
                var listaPersonasEsteMes = _filasVisitasGlobal.Select(f => new Persona { Id = f.PersonaId, NombreComun = f.NombreVisual, Nombre = f.Nombre }).ToList();

                var ventana = new FormularioPersonaWindow(persona, listaPersonasEsteMes);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    if (!ventana.EsUnaFusion()) DatabaseHelper.ActualizarPersonaDesdeVisitas(ventana.PersonaActualizada, fechaActual.ToString("yyyy-MM"));

                    // --- NUEVO: Vaciamos la caché del mapa para obligarlo a recargar y buscar la nueva dirección ---
                    _mapaCargado = false;
                    _htmlMapaCache = "";

                    ActualizarUI_MesMinisterio();
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
                    DatabaseHelper.EliminarPersona(fila.PersonaId);

                    // --- NUEVO: Vaciamos la caché del mapa para que desaparezca la chincheta de la persona borrada ---
                    _mapaCargado = false;
                    _htmlMapaCache = "";

                    ActualizarUI_MesMinisterio();
                }
            }
        }

        // =====================================
        // RESTO DEL CÓDIGO (Territorios y Cursos)
        // =====================================

        private void CmbVinculacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignoramos los SelectionChanged que genera la propia carga de
            // Territorios, pero no los bloqueamos porque Ministerio esté cargando.
            if (_cargandoTerritorios) return;
            if (sender is ComboBox combo && combo.DataContext is CeldaVuelta celda)
            {
                if (!combo.IsLoaded) return;
                if (!combo.IsDropDownOpen && !combo.IsKeyboardFocusWithin) return;

                string nombreSeleccionado = (e.AddedItems.Count > 0) ? e.AddedItems[0].ToString() : (combo.SelectedItem?.ToString() ?? "");
                string nombreGuardar = nombreSeleccionado == "-- Sin Asignar --" ? "" : nombreSeleccionado;

                DatabaseHelper.ActualizarVinculacionTerritorio(celda.RegistroId, nombreGuardar);
                celda.NombreS21 = nombreGuardar;

                ActualizarUI_MesTerritorios();
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

            for (int i = 0; i < 6; i++)
            {
                DateTime m = mesReferencia.AddMonths(-i);
                string mStr = m.ToString("yyyy-MM");
                string mVisual = char.ToUpper(nombresMesesJson[m.Month - 1][0]) + nombresMesesJson[m.Month - 1].Substring(1) + " " + m.Year;

                var datosDB = DatabaseHelper.ObtenerContabilidad(_congreCodigo, mStr);

                historial.Add(new FilaContabilidad
                {
                    MesReferencia = mStr,
                    NombreMesVisual = mVisual,
                    Gastos = datosDB.Gastos == 0 ? "" : datosDB.Gastos.ToString("0.00"),
                    Mantenimiento = datosDB.Mantenimiento == 0 ? "" : datosDB.Mantenimiento.ToString("0.00"),
                    ObraMundial = datosDB.ObraMundial == 0 ? "" : datosDB.ObraMundial.ToString("0.00"),
                    ObraResolucion = datosDB.ObraResolucion == 0 ? "" : datosDB.ObraResolucion.ToString("0.00"),
                    Saldo = datosDB.Saldo == 0 ? "" : datosDB.Saldo.ToString("0.00"),
                    PublicadoresActivos = _totalPublicadoresActivos
                });
            }

            TablaContabilidad.ItemsSource = historial;
        }

        private void TablaContabilidad_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Row.Item is FilaContabilidad fila)
                {
                    fila.ForzarActualizacionPromedio();

                    var datos = new DatabaseHelper.ContabilidadMes
                    {
                        Gastos = ParsearMoneda(fila.Gastos),
                        Mantenimiento = ParsearMoneda(fila.Mantenimiento),
                        ObraMundial = ParsearMoneda(fila.ObraMundial),
                        ObraResolucion = ParsearMoneda(fila.ObraResolucion),
                        Saldo = ParsearMoneda(fila.Saldo)
                    };
                    DatabaseHelper.GuardarContabilidad(_congreCodigo, fila.MesReferencia, datos);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private double ParsearMoneda(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return 0;
            if (double.TryParse(texto.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val)) return val;
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

        private bool EsFechaEnRango(string fechaStr, DateTime inicio, DateTime fin)
        {
            if (string.IsNullOrWhiteSpace(fechaStr)) return false;
            DateTime? fecha = ParsearFecha(fechaStr);
            if (fecha.HasValue) return fecha.Value >= inicio && fecha.Value <= fin;
            return false;
        }

        private CeldaVuelta CrearCeldaVuelta(TerritorioJSON asig, List<string> nombresS21, Dictionary<string, string> cacheFuzzy)
        {
            var celda = new CeldaVuelta { ListaNombresS21 = nombresS21 };
            if (asig == null || string.IsNullOrWhiteSpace(asig.AsignadoA)) return celda;

            celda.EsVisible = Visibility.Visible;
            celda.RegistroId = asig.Id;
            celda.NombreS13 = asig.AsignadoA;
            celda.Fechas = $"{(string.IsNullOrWhiteSpace(asig.FechaAsignado) ? "--" : asig.FechaAsignado)} al {(string.IsNullOrWhiteSpace(asig.FechaCompletado) ? "--" : asig.FechaCompletado)}";

            if (!string.IsNullOrWhiteSpace(asig.NombreVinculado) && nombresS21.Contains(asig.NombreVinculado)) celda.NombreS21 = asig.NombreVinculado;
            else
            {
                if (cacheFuzzy.ContainsKey(asig.AsignadoA)) celda.NombreS21 = cacheFuzzy[asig.AsignadoA];
                else
                {
                    string mejorMatch = ""; int mayorSim = 0, menorDif = 999;
                    foreach (var s21 in nombresS21.Skip(1))
                    {
                        int sim = DatabaseHelper.CalcularSimilitud(asig.AsignadoA, s21);
                        int dif = Math.Abs(asig.AsignadoA.Length - s21.Length);
                        if (sim > mayorSim || (sim == mayorSim && dif < menorDif)) { mayorSim = sim; menorDif = dif; mejorMatch = s21; }
                    }
                    if (mayorSim >= 50) { celda.NombreS21 = mejorMatch; cacheFuzzy[asig.AsignadoA] = mejorMatch; DatabaseHelper.ActualizarVinculacionTerritorio(asig.Id, mejorMatch); }
                    else { cacheFuzzy[asig.AsignadoA] = ""; }
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
            if (sender is Button btn && btn.Tag is DocumentoS303UI docUI)
            {
                if (File.Exists(docUI.DocumentoOriginal.RutaLocal))
                {
                    if (docUI.EsPlantilla)
                    {
                        try
                        {
                            string mesRef = _mesS303.ToString("yyyy-MM");
                            string nombreCopia = $"S-303_{_congreCodigo}_{mesRef}.pdf";
                            string rutaCopia = Path.Combine(Path.GetDirectoryName(docUI.DocumentoOriginal.RutaLocal), nombreCopia);

                            // OBTENER DATOS PARA AUTO-COMPLETAR
                            var congre = DatabaseHelper.ObtenerCongregaciones().FirstOrDefault(c => c.Codigo == _congreCodigo);
                            string fechaVisitaStr = "";

                            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabaseHelper.RutaDirectorioDatos}\\sccosmos.db"))
                            {
                                conn.Open();
                                var cmd = conn.CreateCommand();
                                cmd.CommandText = "SELECT FechaMartes FROM Semanas WHERE Congregacion = @c AND Actividad = 'Visita' AND FechaMartes LIKE @m || '%' LIMIT 1";
                                cmd.Parameters.AddWithValue("@c", _congreNombre);
                                cmd.Parameters.AddWithValue("@m", mesRef);
                                var res = cmd.ExecuteScalar();
                                if (res != null && DateTime.TryParse(res.ToString(), out DateTime fMartes))
                                {
                                    DateTime fDom = fMartes.AddDays(5);
                                    fechaVisitaStr = $"Del {fMartes.Day} de {fMartes.ToString("MMM", new CultureInfo("es-ES"))} al {fDom.Day} de {fDom.ToString("MMM", new CultureInfo("es-ES"))} de {fDom.Year}";
                                }
                            }

                            // OBTENER DATOS DE CONFIGURACIÓN
                            string nomSC = DatabaseHelper.ObtenerConfiguracion("NombreSC", "").Trim();
                            string apeSC = DatabaseHelper.ObtenerConfiguracion("ApellidosSC", "");
                            if (string.IsNullOrEmpty(apeSC)) apeSC = DatabaseHelper.ObtenerConfiguracion("ApellidoSC", ""); // Por si estuviera en singular

                            string nombreCompletoSC = $"{nomSC} {apeSC}".Trim();
                            string numCirc = DatabaseHelper.ObtenerConfiguracion("NumeroCircuito", "");

                            // RELLENAR PDF CON iText7 (Con tamaño de fuente fijo)
                            using (PdfDocument pdfDoc = new PdfDocument(new PdfReader(docUI.DocumentoOriginal.RutaLocal), new PdfWriter(rutaCopia)))
                            {
                                PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, true);
                                if (form != null)
                                {
                                    // Establecemos un tamaño de letra fijo para que no se auto-ajuste de forma extraña
                                    float tamanoLetra = 10f;

                                    var f1 = form.GetField("900_1_Text_SanSerif");
                                    if (f1 != null) { f1.SetFontSize(tamanoLetra); f1.SetValue(_congreCodigo ?? ""); }

                                    var f2 = form.GetField("900_2_Text_SanSerif");
                                    if (f2 != null) { f2.SetFontSize(tamanoLetra); f2.SetValue(fechaVisitaStr ?? ""); }

                                    var f3 = form.GetField("900_3_Text_SanSerif");
                                    if (f3 != null) { f3.SetFontSize(tamanoLetra); f3.SetValue(_congreNombre ?? ""); }

                                    var f4 = form.GetField("900_4_Text_SanSerif");
                                    if (f4 != null) { f4.SetFontSize(tamanoLetra); f4.SetValue(congre?.Municipio ?? ""); }

                                    var f5 = form.GetField("900_5_Text_SanSerif");
                                    if (f5 != null) { f5.SetFontSize(tamanoLetra); f5.SetValue(congre?.Provincia ?? ""); }

                                    var f6 = form.GetField("900_6_Text_SanSerif");
                                    if (f6 != null) { f6.SetFontSize(tamanoLetra); f6.SetValue(nombreCompletoSC); }

                                    var f7 = form.GetField("900_7_Text_SanSerif");
                                    if (f7 != null) { f7.SetFontSize(tamanoLetra); f7.SetValue(numCirc ?? ""); }
                                }
                            }

                            DatabaseHelper.InsertarDocumento("S-303", nombreCopia, rutaCopia);

                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rutaCopia) { UseShellExecute = true });
                            ActualizarUI_MesS303();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error al crear y rellenar la copia del S-303: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(docUI.DocumentoOriginal.RutaLocal) { UseShellExecute = true });
                    }
                }
                else
                {
                    MessageBox.Show("El archivo no se encuentra en el disco.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var plantilla = DatabaseHelper.ObtenerDocumentos("Envío de S-303")
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
            var recomendaciones = DatabaseHelper.ObtenerRecomendaciones()
                .Where(r => r.Congregacion == _congreNombre)
                .Select(r => new RecomendacionS2UI { Original = r, Aprobar = false })
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

            // Extraemos las recomendaciones (S-2) que el usuario haya marcado con el CheckBox para aprobar
            var recomendacionesS2 = TablaS2.ItemsSource as List<RecomendacionS2UI>;
            var recomendacionesAAprobar = recomendacionesS2?.Where(r => r.Aprobar).Select(r => r.Original).ToList() ?? new List<RecomendacionHub>();

            if (PanelCargando != null) PanelCargando.Visibility = Visibility.Visible;

            try
            {
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

                    // 2. GESTIÓN DE S-2 (Futuro)
                    if (recomendacionesAAprobar.Count > 0)
                    {
                        // Si más adelante programas la lógica para que los S-2 marcados con Check se aprueben, irá aquí.
                        // SincronizadorJWHub.AprobarRecomendacionMasiva(folderPath, recomendacionesAAprobar); 
                    }

                });

                MessageBox.Show("El correo con el S-303 se ha enviado con éxito.", "Envío completado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ha ocurrido un error al intentar enviar el correo:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
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

            private bool _aprobar;
            public bool Aprobar
            {
                get { return _aprobar; }
                set { _aprobar = value; OnPropertyChanged("Aprobar"); }
            }

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

            public string CursosUltimoMes { get; set; }
            public string TerritoriosTrabajados { get; set; }
            public bool EsPrecursor { get; set; }
        }

        public class FilaTablaTerritorio
        {
            public string Nombre { get; set; }
            public string UltimaVez { get; set; }

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
            public List<string> ListaNombresS21 { get; set; }
            public Visibility EsVisible { get; set; } = Visibility.Collapsed;
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
            public string Texto { get; set; }
            public string Dia { get; set; }
            public string Turno { get; set; }
            public string Responsable { get; set; }
            public DatabaseHelper.ActividadPrograma ActividadDB { get; set; }
        }

        public class FilaContabilidad : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            public void ForzarActualizacionPromedio() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Promedio"));

            public string MesReferencia { get; set; }
            public string NombreMesVisual { get; set; }
            public string Gastos { get; set; }
            public string Mantenimiento { get; set; }
            public string ObraMundial { get; set; }
            public string ObraResolucion { get; set; }
            public string Saldo { get; set; }
            public int PublicadoresActivos { get; set; }

            public string Promedio
            {
                get
                {
                    double suma = ParsearMonedaHelper(Gastos) + ParsearMonedaHelper(Mantenimiento) + ParsearMonedaHelper(ObraMundial) + ParsearMonedaHelper(ObraResolucion);
                    if (PublicadoresActivos <= 0) return "0.00 €";
                    return (suma / PublicadoresActivos).ToString("F2") + " €";
                }
            }

            private double ParsearMonedaHelper(string texto)
            {
                if (string.IsNullOrWhiteSpace(texto)) return 0;
                if (double.TryParse(texto.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double val)) return val;
                return 0;
            }
        }
    }
}