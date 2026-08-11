using System;
using System.Collections.Generic;
using System.Linq;
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
        private string _mesVisitaActiva = null;
        private int _totalPublicadoresActivos = 0;
        private string _mesContabilidadActivo = "";

        public static string CodigoPendiente = "";
        public static string NombrePendiente = "";
        public static int IdVisitaPendiente = 0;

        private bool _cargandoDatos = false;

        private List<FilaVisitaPublisher> _filasVisitasGlobal = new List<FilaVisitaPublisher>();
        private List<FilaTablaTerritorio> _territoriosGlobal = new List<FilaTablaTerritorio>();
        private int _paginaActual = 1;
        private const int _itemsPorPagina = 20;

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

            CargarGruposPredicacion();
            CargarCintaMeses();

            // Ajustamos las columnas por primera vez con el ancho inicial
            AjustarVisibilidadColumnas(this.ActualWidth);
        }

        // =========================================================
        // DISEÑO RESPONSIVO (Ocultar columnas al achicar pantalla)
        // =========================================================
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

                // Si la pantalla baja de 1250, ocultamos el Nombre de JW Hub (que suele sobrar)
                if (header == "Nombre JW Hub")
                    col.Visibility = anchoPantalla < 1250 ? Visibility.Collapsed : Visibility.Visible;

                // Si baja de 1100, ocultamos detalles secundarios demográficos
                else if (header == "Sexo" || header == "Edad" || header == "Baut.")
                    col.Visibility = anchoPantalla < 1100 ? Visibility.Collapsed : Visibility.Visible;

                // Si baja de 950, ocultamos conteos estadísticos
                else if (header == "Terrs." || header == "CBs")
                    col.Visibility = anchoPantalla < 950 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void UIElement_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };

                if (MainScrollViewer != null)
                {
                    MainScrollViewer.RaiseEvent(eventArg);
                }
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
                    if (double.TryParse(act.Horas?.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double hMes))
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

        private void CargarCintaMeses()
        {
            var meses = new List<MesSelectorVisita>();
            DateTime inicio = DateTime.Today.AddMonths(-24);

            for (int i = 0; i <= 36; i++)
            {
                DateTime f = inicio.AddMonths(i);
                string nombreMes = f.ToString("MMMM yyyy", new System.Globalization.CultureInfo("es-ES"));
                nombreMes = char.ToUpper(nombreMes[0]) + nombreMes.Substring(1);
                meses.Add(new MesSelectorVisita { FechaBase = f, NombreVisual = nombreMes });
            }
            ListMeses.ItemsSource = meses;

            DateTime maxFecha = CalcularMaximoMesActividad();

            var mesActual = maxFecha != DateTime.MinValue
                ? meses.FirstOrDefault(m => m.FechaBase.Year == maxFecha.Year && m.FechaBase.Month == maxFecha.Month)
                : meses.FirstOrDefault(m => m.FechaBase.Year == DateTime.Today.Year && m.FechaBase.Month == DateTime.Today.Month);

            if (mesActual == null) mesActual = meses.FirstOrDefault(m => m.FechaBase.Year == DateTime.Today.Year && m.FechaBase.Month == DateTime.Today.Month);

            if (mesActual != null)
            {
                ListMeses.SelectedItem = mesActual;
            }
        }

        private void BtnScrollIzquierda_Click(object sender, RoutedEventArgs e)
        {
            ScrollMeses.ScrollToHorizontalOffset(ScrollMeses.HorizontalOffset - 250);
        }

        private void BtnScrollDerecha_Click(object sender, RoutedEventArgs e)
        {
            ScrollMeses.ScrollToHorizontalOffset(ScrollMeses.HorizontalOffset + 250);
        }

        private void CentrarMesEnCinta(MesSelectorVisita mesElegido)
        {
            if (mesElegido == null || ScrollMeses == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = ListMeses.ItemContainerGenerator.ContainerFromItem(mesElegido) as FrameworkElement;
                if (container != null && ScrollMeses.ViewportWidth > 0)
                {
                    var transform = container.TransformToAncestor(ListMeses);
                    Point posicionAbsoluta = transform.Transform(new Point(0, 0));
                    double offsetCentro = posicionAbsoluta.X - (ScrollMeses.ViewportWidth / 2) + (container.ActualWidth / 2);
                    ScrollMeses.ScrollToHorizontalOffset(offsetCentro);
                }
            }), DispatcherPriority.Loaded);
        }

        private async void ListMeses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListMeses.SelectedItem is MesSelectorVisita seleccion)
            {
                CentrarMesEnCinta(seleccion);

                if (string.IsNullOrEmpty(_congreCodigo)) return;
                if (_cargandoDatos) return;

                _cargandoDatos = true;
                try
                {
                    if (PanelCargando != null) PanelCargando.Visibility = Visibility.Visible;
                    await Task.Delay(50);

                    DateTime mesReferencia = new DateTime(seleccion.FechaBase.Year, seleccion.FechaBase.Month, 1);

                    DateTime maxMesActividad = CalcularMaximoMesActividad();
                    DateTime mesFinGrid = mesReferencia;

                    if (maxMesActividad != DateTime.MinValue && mesReferencia > maxMesActividad)
                    {
                        mesFinGrid = maxMesActividad;
                    }

                    DateTime[] mesesARenderizar = new DateTime[6];
                    for (int i = 0; i < 6; i++) mesesARenderizar[5 - i] = mesFinGrid.AddMonths(-i);

                    DateTime fechaInicio6Meses = mesReferencia.AddMonths(-5);
                    DateTime fechaFin6Meses = mesReferencia.AddMonths(1).AddDays(-1);

                    await ProcesarColumnasYDatosDinamicos(mesesARenderizar);
                    await CargarTerritorios(mesReferencia, fechaInicio6Meses, fechaFin6Meses);

                    CargarProgramaYNotas(mesReferencia);
                    CargarContabilidadList(mesReferencia);
                }
                finally
                {
                    _cargandoDatos = false;
                    if (PanelCargando != null) PanelCargando.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void CargarProgramaYNotas(DateTime mesReferencia)
        {
            string mesRefStr = mesReferencia.ToString("yyyy-MM");

            bool hayVisitaEnEsteMes = DatabaseHelper.HayVisitaRegistrada(_congreCodigo, _congreNombre, mesRefStr);

            if (hayVisitaEnEsteMes)
            {
                _mesVisitaActiva = mesRefStr;
                ExpanderPrograma.Visibility = Visibility.Visible;
                ExpanderPrograma.Header = $"PROGRAMA DE LA VISITA ({mesReferencia.ToString("MMMM yyyy").ToUpper()})";
                ExpanderNotas.Visibility = Visibility.Visible;

                string[] turnos = { "1. MAÑANA", "2. MEDIODÍA", "3. TARDE", "4. NOCHE" };
                var filasPrograma = new List<FilaPrograma>();

                foreach (var turno in turnos)
                {
                    if (turno == "2. MEDIODÍA")
                        filasPrograma.Add(CrearFilaPrograma(_mesVisitaActiva, turno, "Comida"));
                    else
                    {
                        filasPrograma.Add(CrearFilaPrograma(_mesVisitaActiva, turno, "Joni"));
                        filasPrograma.Add(CrearFilaPrograma(_mesVisitaActiva, turno, "Rebe"));
                    }
                }

                var collectionView = CollectionViewSource.GetDefaultView(filasPrograma);
                collectionView.GroupDescriptions.Clear();
                collectionView.GroupDescriptions.Add(new PropertyGroupDescription("Turno"));
                TablaPrograma.ItemsSource = collectionView;

                IdVisitaPendiente = DatabaseHelper.ObtenerIdSemanaVisita(_congreNombre, mesRefStr);

                if (IdVisitaPendiente == 0)
                {
                    var nuevaSemana = new SemanaCalendario { FechaMartes = new DateTime(mesReferencia.Year, mesReferencia.Month, 1), Congregacion = _congreNombre, Actividad = "Visita" };
                    DatabaseHelper.GuardarSemana(nuevaSemana);
                    IdVisitaPendiente = DatabaseHelper.ObtenerIdSemanaVisita(_congreNombre, mesRefStr);
                }

                if (IdVisitaPendiente > 0)
                {
                    var notasPlanas = DatabaseHelper.ObtenerNotasVisita(IdVisitaPendiente);

                    MisNotasAgrupadas = notasPlanas.GroupBy(n => n.Cabecera)
                        .Select(g => new CategoriaNotas
                        {
                            Cabecera = g.Key,
                            Preguntas = g.ToList()
                        }).ToList();

                    var panelNotas = this.FindName("PanelNotas") as FrameworkElement;
                    if (panelNotas != null) panelNotas.DataContext = this;
                }
            }
            else
            {
                _mesVisitaActiva = null;
                ExpanderPrograma.Visibility = Visibility.Collapsed;
                ExpanderNotas.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtNotaRespuesta_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt && txt.DataContext is NotaVisita nota)
            {
                DatabaseHelper.ActualizarNotaVisitaUnica(nota);
            }
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

                string texto = responsable == "Comida"
                    ? $"{act.Hora} - {nombreComp}"
                    : $"{act.Hora} - {act.TipoActividad} ({nombreComp})";

                celda.ListaActividades.Add(new BotonActividadUI { Texto = texto });
            }

            if (responsable == "Comida" && celda.ListaActividades.Count > 0)
                celda.VerBotonAnadir = Visibility.Collapsed;
            else
                celda.VerBotonAnadir = Visibility.Visible;

            return celda;
        }

        private void BtnProgramarActividad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CeldaPrograma celda)
            {
                if (ListMeses.SelectedItem == null || string.IsNullOrEmpty(_mesVisitaActiva)) return;
                DateTime fechaActual = ((MesSelectorVisita)ListMeses.SelectedItem).FechaBase;

                var ventana = new ProgramarActividadWindow(celda.Responsable, celda.Turno, celda.Dia, _congreCodigo, _mesVisitaActiva);
                if (ventana.ShowDialog() == true)
                {
                    CargarProgramaYNotas(fechaActual);
                }
            }
        }

        private void CargarGruposPredicacion()
        {
            var listadoHermanos = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo);
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

        private async Task ProcesarColumnasYDatosDinamicos(DateTime[] meses)
        {
            TablaVisitas.Columns.Clear();

            TablaVisitas.AlternatingRowBackground = null;
            TablaVisitas.RowBackground = Brushes.Transparent;

            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            rowStyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));

            rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(4, 0, 0, 0)));
            rowStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));

            var precursorRowTrigger = new DataTrigger { Binding = new Binding("EsPrecursor"), Value = true };
            precursorRowTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, (SolidColorBrush)new BrushConverter().ConvertFrom("#0078D4")));
            rowStyle.Triggers.Add(precursorRowTrigger);

            var hoverTrigger = new Trigger { Property = DataGridRow.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(80, 0, 120, 212))));
            rowStyle.Triggers.Add(hoverTrigger);

            rowStyle.Setters.Add(new EventSetter(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(FilaVisita_Click)));
            TablaVisitas.RowStyle = rowStyle;

            var baseCellStyle = new Style(typeof(DataGridCell));
            baseCellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            baseCellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            baseCellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 0, 0, 0)));

            var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            baseCellStyle.Triggers.Add(selectedTrigger);

            TablaVisitas.CellStyle = baseCellStyle;

            var estiloIzquierda = new Style(typeof(TextBlock));
            estiloIzquierda.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            estiloIzquierda.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left));
            estiloIzquierda.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));

            var estiloNombre = new Style(typeof(TextBlock), estiloIzquierda);
            var triggerSinAlerta = new DataTrigger { Binding = new Binding("AlertaTooltip"), Value = null };
            triggerSinAlerta.Setters.Add(new Setter(ToolTipService.IsEnabledProperty, false));
            estiloNombre.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("AlertaTooltip")));
            estiloNombre.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 15000));
            estiloNombre.Triggers.Add(triggerSinAlerta);

            var triggerPrecursorTxt = new DataTrigger { Binding = new Binding("EsPrecursor"), Value = true };
            triggerPrecursorTxt.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            estiloNombre.Triggers.Add(triggerPrecursorTxt);

            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "Nombre", Binding = new Binding("NombreVisual"), Width = new DataGridLength(16, DataGridLengthUnitType.Star), ElementStyle = estiloNombre });
            TablaVisitas.Columns.Add(new DataGridTextColumn { Header = "Nombre JW Hub", Binding = new Binding("NombreJWHub"), Width = new DataGridLength(14, DataGridLengthUnitType.Star), ElementStyle = estiloIzquierda });

            string[] shortMonths = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

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

            // Ajustamos las columnas recién creadas al tamaño de la pantalla
            AjustarVisibilidadColumnas(this.ActualWidth);

            var listadoHermanos = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo);
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
                if (fElim.HasValue && new DateTime(fElim.Value.Year, fElim.Value.Month, 1) < mesReferenciaGrid)
                    continue;

                DateTime? fCrea = ParsearFecha(h.FechaCreacion);
                if (fCrea.HasValue && new DateTime(fCrea.Value.Year, fCrea.Value.Month, 1) > mesReferenciaGrid)
                    continue;
                contadorRespiro++;
                if (contadorRespiro % 10 == 0) await Task.Delay(1);

                string nombreLimpio = !string.IsNullOrWhiteSpace(h.Nombre) ? h.Nombre.Trim() : "";
                string jwHubLimpio = !string.IsNullOrWhiteSpace(h.NombreJWHub) ? h.NombreJWHub.Trim() : "";
                string nombramientoLimpio = !string.IsNullOrWhiteSpace(h.Nombramiento) ? h.Nombramiento.Trim() : "";
                string servicioLimpio = !string.IsNullOrWhiteSpace(h.Servicio) ? h.Servicio.Trim() : "";
                string sexoLimpio = !string.IsNullOrWhiteSpace(h.Sexo) ? h.Sexo.Trim() : "-";

                bool esPrec = !string.IsNullOrWhiteSpace(servicioLimpio) && (servicioLimpio.ToLower().Contains("precursor") || servicioLimpio.ToLower().Contains("misionero"));

                var informesHistoricos = DatabaseHelper.ObtenerActividadPersona(h.Id);
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

                string nombreMostrar = nombreLimpio;
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
                    Nombre = nombreLimpio,
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

            _filasVisitasGlobal = filasGrid.OrderBy(f => (f.Nombre ?? "").Replace("(", "").Replace(")", "").Trim()).ThenBy(f => f.FechaNacimientoReal).ToList();
            var gruposUnicos = _filasVisitasGlobal.Select(f => f.GrupoPredicacion).Distinct().OrderBy(g => g).ToList();

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
                        col.Width = 0;
                        col.Width = anchoActual;
                    }
                }), DispatcherPriority.Loaded);
            }
            else
            {
                TablaVisitas.ItemsSource = null;
            }
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
                        col.Width = 0;
                        col.Width = anchoActual;
                    }
                }), DispatcherPriority.Loaded);
            }
        }

        private async Task CargarTerritorios(DateTime mesReferencia, DateTime fechaInicio, DateTime fechaFin)
        {
            var listaPlana = DatabaseHelper.ObtenerTerritoriosPorCongregacion(_congreCodigo);
            var grupos = listaPlana.GroupBy(t => t.Nombre).ToList();

            var nombresS21Disponibles = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo)
                .Where(p => p.Grupo > 0 || string.IsNullOrWhiteSpace(p.NombreJWHub) || p.Nombre != p.NombreJWHub)
                .Select(p => p.Nombre).OrderBy(n => n).ToList();
            nombresS21Disponibles.Insert(0, "-- Sin Asignar --");

            var filasGrid = new List<FilaTablaTerritorio>();

            DateTime fechaBaseStats = mesReferencia;
            DateTime fecha6MesesAntes = fechaBaseStats.AddMonths(-6);
            DateTime fecha4MesesAntes = fechaBaseStats.AddMonths(-4);

            int totalTerritorios = 0, trabajados6Meses = 0, trabajandoseAhora = 0;
            int caducados = 0, sinTrabajar1Ano = 0, sinTrabajar2Anos = 0;

            var cacheFuzzy = new Dictionary<string, string>();
            int contadorRespiro = 0;

            foreach (var grupo in grupos)
            {
                contadorRespiro++;
                if (contadorRespiro % 10 == 0) await Task.Delay(1);

                DateTime? fElim = ParsearFecha(grupo.FirstOrDefault()?.FechaEliminacion);
                if (fElim.HasValue && new DateTime(fElim.Value.Year, fElim.Value.Month, 1) < fechaBaseStats)
                    continue;

                DateTime? fechaCreacion = ParsearFecha(grupo.FirstOrDefault()?.FechaCreacion);
                if (fechaCreacion.HasValue && new DateTime(fechaCreacion.Value.Year, fechaCreacion.Value.Month, 1) > fechaBaseStats)
                    continue;

                totalTerritorios++;

                var asignaciones = grupo.ToList();
                var fila = new FilaTablaTerritorio { Nombre = grupo.Key, UltimaVez = grupo.FirstOrDefault()?.UltimaFechaCompletado };

                var asignacionesFiltradas = asignaciones.Where(a => EsFechaEnRango(a.FechaAsignado, fechaInicio, fechaFin)).ToList();
                fila.Vuelta1 = CrearCeldaVuelta(asignacionesFiltradas.ElementAtOrDefault(0), nombresS21Disponibles, cacheFuzzy);
                fila.Vuelta2 = CrearCeldaVuelta(asignacionesFiltradas.ElementAtOrDefault(1), nombresS21Disponibles, cacheFuzzy);
                fila.Vuelta3 = CrearCeldaVuelta(asignacionesFiltradas.ElementAtOrDefault(2), nombresS21Disponibles, cacheFuzzy);
                fila.Vuelta4 = CrearCeldaVuelta(asignacionesFiltradas.ElementAtOrDefault(3), nombresS21Disponibles, cacheFuzzy);

                filasGrid.Add(fila);

                bool trabajadoEn6Meses = false;
                bool enCurso = false;
                bool caducado = false;
                DateTime? ultimaDevolucionValida = null;

                foreach (var asig in asignaciones)
                {
                    DateTime? fAsig = ParsearFecha(asig.FechaAsignado);
                    DateTime? fComp = ParsearFecha(asig.FechaCompletado);

                    if (fComp.HasValue && fComp.Value >= fecha6MesesAntes && fComp.Value < fechaBaseStats)
                        trabajadoEn6Meses = true;

                    if (fAsig.HasValue && fAsig.Value < fechaBaseStats && (!fComp.HasValue || fComp.Value >= fechaBaseStats))
                    {
                        enCurso = true;
                        if (fAsig.Value <= fecha4MesesAntes) caducado = true;
                    }

                    if (fComp.HasValue && fComp.Value < fechaBaseStats)
                    {
                        if (!ultimaDevolucionValida.HasValue || fComp.Value > ultimaDevolucionValida.Value)
                            ultimaDevolucionValida = fComp.Value;
                    }
                }

                if (trabajadoEn6Meses) trabajados6Meses++;
                if (enCurso) trabajandoseAhora++;
                if (caducado) caducados++;

                DateTime fechaReferenciaParaInactividad = ultimaDevolucionValida ?? fechaCreacion ?? DateTime.MinValue;

                if (fechaReferenciaParaInactividad != DateTime.MinValue)
                {
                    double diasInactivo = (fechaBaseStats - fechaReferenciaParaInactividad).TotalDays;
                    if (diasInactivo >= 730) sinTrabajar2Anos++;
                    else if (diasInactivo >= 365) sinTrabajar1Ano++;
                }
                else
                {
                    sinTrabajar2Anos++;
                }
            }

            _territoriosGlobal = filasGrid;
            MostrarPaginaTerritorios(1);

            if (TxtTotalTerritorios != null) TxtTotalTerritorios.Text = totalTerritorios.ToString();
            if (TxtTerritoriosTrabajados != null) TxtTerritoriosTrabajados.Text = trabajados6Meses.ToString();
            if (TxtTerritoriosEnCurso != null) TxtTerritoriosEnCurso.Text = trabajandoseAhora.ToString();
            if (TxtTerritoriosCaducados != null) TxtTerritoriosCaducados.Text = caducados.ToString();
            if (TxtTerritorios1Ano != null) TxtTerritorios1Ano.Text = sinTrabajar1Ano.ToString();
            if (TxtTerritorios2Anos != null) TxtTerritorios2Anos.Text = sinTrabajar2Anos.ToString();
        }

        private void MostrarPaginaTerritorios(int pagina)
        {
            if (_territoriosGlobal == null || !_territoriosGlobal.Any()) return;

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
                    Style = (Style)this.FindResource("EstiloBotonPaginacion")
                };

                if (i == _paginaActual)
                    btn.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#0078D4");
                else
                    btn.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#333333");

                btn.Click += BtnPaginaTerritorio_Click;
                PanelPaginacion.Children.Add(btn);
            }
        }

        private void BtnPaginaTerritorio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int paginaTarget)
            {
                MostrarPaginaTerritorios(paginaTarget);
            }
        }

        private void FilaVisita_Click(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;
            while (originalSource != null)
            {
                if (originalSource is Button || originalSource is System.Windows.Controls.Primitives.ScrollBar) return;
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            if (sender is DataGridRow row && row.DataContext is FilaVisitaPublisher fila)
            {
                if (ListMeses.SelectedItem == null) return;
                DateTime fechaActual = ((MesSelectorVisita)ListMeses.SelectedItem).FechaBase;

                var ventana = new EditarPersonaWindow(fila.PersonaId, _congreCodigo, fechaActual);
                if (ventana.ShowDialog() == true)
                {
                    CargarGruposPredicacion();
                    ListMeses_SelectionChanged(null, null);
                }
            }
        }

        private void BtnEditarPersona_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FilaVisitaPublisher fila)
            {
                if (ListMeses.SelectedItem == null) return;
                DateTime fechaActual = ((MesSelectorVisita)ListMeses.SelectedItem).FechaBase;

                var ventana = new EditarPersonaWindow(fila.PersonaId, _congreCodigo, fechaActual);
                if (ventana.ShowDialog() == true)
                {
                    CargarGruposPredicacion();
                    ListMeses_SelectionChanged(null, null);
                }
            }
        }

        private void BtnEliminarPersona_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FilaVisitaPublisher fila)
            {
                if (MessageBox.Show($"¿Estás seguro de eliminar a '{fila.Nombre}'?\n\nSus registros históricos se mantendrán intactos, pero desaparecerá del listado a partir de este mes.", "Eliminar Persona", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    DatabaseHelper.MarcarPersonaEliminada(fila.PersonaId);
                    ListMeses_SelectionChanged(null, null);
                }
            }
        }

        private void BtnNuevaPersona_Click(object sender, RoutedEventArgs e)
        {
            if (ListMeses.SelectedItem == null) return;
            DateTime fechaActual = ((MesSelectorVisita)ListMeses.SelectedItem).FechaBase;

            var ventana = new EditarPersonaWindow(0, _congreCodigo, fechaActual);
            if (ventana.ShowDialog() == true)
            {
                CargarGruposPredicacion();
                ListMeses_SelectionChanged(null, null);
            }
        }

        private void CmbVinculacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoDatos) return;
            if (sender is ComboBox combo && combo.DataContext is CeldaVuelta celda)
            {
                if (!combo.IsLoaded) return;

                if (!combo.IsDropDownOpen && !combo.IsKeyboardFocusWithin) return;

                string nombreSeleccionado = (e.AddedItems.Count > 0) ? e.AddedItems[0].ToString() : (combo.SelectedItem?.ToString() ?? "");
                string nombreGuardar = nombreSeleccionado == "-- Sin Asignar --" ? "" : nombreSeleccionado;

                DatabaseHelper.ActualizarVinculacionTerritorio(celda.RegistroId, nombreGuardar);
                celda.NombreS21 = nombreGuardar;

                if (ListMeses != null && ListMeses.SelectedItem != null) ListMeses_SelectionChanged(null, null);
            }
        }

        private void BtnEliminarTerritorio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FilaTablaTerritorio fila)
            {
                if (MessageBox.Show($"¿Estás seguro de eliminar el territorio '{fila.Nombre}'?\n\nNo se borrarán los datos del pasado, pero dejará de aparecer y contabilizar a partir de este mes.", "Eliminar Territorio", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    DatabaseHelper.MarcarTerritorioEliminado(_congreCodigo, fila.Nombre);
                    ListMeses_SelectionChanged(null, null);
                }
            }
        }

        private void BtnEditarTerritorio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FilaTablaTerritorio fila)
            {
                var inputWin = new Window
                {
                    Title = "Renombrar Territorio",
                    Width = 350,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    WindowStyle = WindowStyle.ToolWindow,
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
                };

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

                if (inputWin.ShowDialog() == true)
                {
                    ListMeses_SelectionChanged(null, null);
                }
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
            if (double.TryParse(texto.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                return val;
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
            else if (registro.Participacion)
            {
                valorVisual = "✅";
                colorFinal = colorVerde;
            }
            else
            {
                colorFinal = colorRojo;
            }

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

            if (!string.IsNullOrWhiteSpace(asig.NombreVinculado) && nombresS21.Contains(asig.NombreVinculado))
            {
                celda.NombreS21 = asig.NombreVinculado;
            }
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

        // ===============================================
        // ESTRUCTURAS DE DATOS (CLASES INTERNAS)
        // ===============================================

        public class MesSelectorVisita
        {
            public DateTime FechaBase { get; set; }
            public string NombreVisual { get; set; }
        }

        public class FilaVisitaPublisher
        {
            public int PersonaId { get; set; }
            public string Nombre { get; set; }
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
            public CeldaVuelta Vuelta1 { get; set; }
            public CeldaVuelta Vuelta2 { get; set; }
            public CeldaVuelta Vuelta3 { get; set; }
            public CeldaVuelta Vuelta4 { get; set; }
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
        }

        public class FilaContabilidad : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            public void ForzarActualizacionPromedio() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Promedio"));

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
                if (double.TryParse(texto.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val)) return val;
                return 0;
            }
        }
    }
}