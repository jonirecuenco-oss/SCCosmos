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
using System.Text.Json;
using System.Net.Http;
using System.Globalization;
using System.IO;

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
            //if (TxtMesPrograma != null) TxtMesPrograma.Text = FormatearMes(_mesPrograma);
            if (!_cargandoDatos) CargarPrograma(_mesPrograma);
        }

        // --- NOTAS ---
        private void ActualizarUI_MesNotas()
        {
            //if (TxtMesNotas != null) TxtMesNotas.Text = FormatearMes(_mesNotas);
            if (!_cargandoDatos) CargarNotas(_mesNotas);
        }

        // --- TERRITORIOS ---
        private async void ActualizarUI_MesTerritorios()
        {
            if (TxtMesTerritorios != null) TxtMesTerritorios.Text = FormatearMes(_mesTerritorios);
            if (_cargandoDatos) return;
            _cargandoDatos = true;
            try
            {
                DateTime inicio6m = _mesTerritorios.AddMonths(-5);
                DateTime fin6m = _mesTerritorios.AddMonths(1).AddDays(-1);

                var listadoHermanosHistorico = DatabaseHelper.ObtenerPersonasHistoricasPorMes(_congreCodigo, _mesTerritorios.ToString("yyyy-MM"));
                await CargarTerritorios(_mesTerritorios, inicio6m, fin6m, listadoHermanosHistorico);
            }
            finally { _cargandoDatos = false; }
        }
        private void BtnMesAntTerritorios_Click(object sender, RoutedEventArgs e) { _mesTerritorios = _mesTerritorios.AddMonths(-1); ActualizarUI_MesTerritorios(); }
        private void BtnMesSigTerritorios_Click(object sender, RoutedEventArgs e) { _mesTerritorios = _mesTerritorios.AddMonths(1); ActualizarUI_MesTerritorios(); }

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
            //if (TxtMesS303 != null) TxtMesS303.Text = FormatearMes(_mesS303);
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
                if (panelNotas != null) panelNotas.DataContext = this;
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

                string texto = responsable == "Comida" || responsable == "Cenar" || responsable == "Dormir" ? $"{act.Hora} - {nombreComp}" : $"{act.Hora} - {act.TipoActividad} ({nombreComp})";
                celda.ListaActividades.Add(new BotonActividadUI { Texto = texto, Dia = dia, Turno = turnoLimpio, Responsable = responsable, ActividadDB = act });
            }

            if ((responsable == "Comida" || responsable == "Cenar" || responsable == "Dormir") && celda.ListaActividades.Count > 0) celda.VerBotonAnadir = Visibility.Collapsed;
            else celda.VerBotonAnadir = Visibility.Visible;

            return celda;
        }

        private void BtnProgramarActividad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CeldaPrograma celda)
            {
                var ventana = new ProgramarActividadWindow(celda.Responsable, celda.Turno, celda.Dia, _congreCodigo, _mesPrograma.ToString("yyyy-MM"));
                if (ventana.ShowDialog() == true) ActualizarUI_MesPrograma();
            }
        }

        private void BtnEliminarActividad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BotonActividadUI ui)
            {
                if (MessageBox.Show($"¿Eliminar actividad de {ui.Responsable} a las {ui.ActividadDB.Hora}?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabaseHelper.RutaDirectorioDatos}\\sccosmos.db"))
                    {
                        connection.Open();
                        var cmd = connection.CreateCommand();
                        cmd.CommandText = "DELETE FROM ProgramaVisitaV2 WHERE CongreCodigo=@c AND MesReferencia=@m AND DiaSemana=@d AND Turno=@t AND Responsable=@r AND Hora=@h";
                        cmd.Parameters.AddWithValue("@c", _congreCodigo);
                        cmd.Parameters.AddWithValue("@m", _mesPrograma.ToString("yyyy-MM"));
                        cmd.Parameters.AddWithValue("@d", ui.Dia);
                        cmd.Parameters.AddWithValue("@t", ui.Turno);
                        cmd.Parameters.AddWithValue("@r", ui.Responsable);
                        cmd.Parameters.AddWithValue("@h", ui.ActividadDB.Hora);
                        cmd.ExecuteNonQuery();
                    }
                    ActualizarUI_MesPrograma();
                }
            }
        }

        private void BtnEditarActividad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BotonActividadUI ui)
            {
                var inputWin = new Window { Title = "Editar Actividad", Width = 350, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterScreen, WindowStyle = WindowStyle.ToolWindow, Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)) };
                var grid = new StackPanel { Margin = new Thickness(20) };

                // 1. Selector de Acompañante
                grid.Children.Add(new TextBlock { Text = "Acompañante / Familia:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
                var listaHermanos = DatabaseHelper.ObtenerPersonasHistoricasPorMes(_congreCodigo, _mesPrograma.ToString("yyyy-MM")).OrderBy(p => p.Nombre).ToList();
                var cmbAcompanante = new ComboBox
                {
                    ItemsSource = listaHermanos,
                    DisplayMemberPath = "Nombre",
                    SelectedValuePath = "Id",
                    Padding = new Thickness(8),
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    SelectedValue = ui.ActividadDB.CompaneroId
                };
                grid.Children.Add(cmbAcompanante);

                // 2. Hora
                grid.Children.Add(new TextBlock { Text = "Hora (ej. 10:00):", Foreground = Brushes.White, Margin = new Thickness(0, 10, 0, 5) });
                var txtHora = new TextBox { Text = ui.ActividadDB.Hora, Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
                grid.Children.Add(txtHora);

                // 3. Tipo de Actividad
                grid.Children.Add(new TextBlock { Text = "Tipo de Actividad / Notas:", Foreground = Brushes.White, Margin = new Thickness(0, 10, 0, 5) });
                var txtTipo = new TextBox { Text = ui.ActividadDB.TipoActividad, Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
                grid.Children.Add(txtTipo);

                var btnSave = new Button { Content = "Guardar Cambios", Width = 120, Margin = new Thickness(0, 15, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)), Foreground = Brushes.White, Padding = new Thickness(8), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                btnSave.Click += (s, args) => {
                    if (!string.IsNullOrWhiteSpace(txtHora.Text) && cmbAcompanante.SelectedValue != null)
                    {
                        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabaseHelper.RutaDirectorioDatos}\\sccosmos.db"))
                        {
                            connection.Open();
                            var cmd = connection.CreateCommand();
                            cmd.CommandText = "UPDATE ProgramaVisitaV2 SET Hora=@nh, TipoActividad=@nt, CompaneroId=@ncomp WHERE CongreCodigo=@c AND MesReferencia=@m AND DiaSemana=@d AND Turno=@t AND Responsable=@r AND Hora=@h";
                            cmd.Parameters.AddWithValue("@nh", txtHora.Text.Trim());
                            cmd.Parameters.AddWithValue("@nt", txtTipo.Text.Trim());
                            cmd.Parameters.AddWithValue("@ncomp", cmbAcompanante.SelectedValue);
                            cmd.Parameters.AddWithValue("@c", _congreCodigo);
                            cmd.Parameters.AddWithValue("@m", _mesPrograma.ToString("yyyy-MM"));
                            cmd.Parameters.AddWithValue("@d", ui.Dia);
                            cmd.Parameters.AddWithValue("@t", ui.Turno);
                            cmd.Parameters.AddWithValue("@r", ui.Responsable);
                            cmd.Parameters.AddWithValue("@h", ui.ActividadDB.Hora);
                            cmd.ExecuteNonQuery();
                        }
                        inputWin.DialogResult = true;
                        inputWin.Close();
                    }
                    else
                    {
                        MessageBox.Show("Debes indicar una hora y seleccionar un acompañante.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };
                grid.Children.Add(btnSave);
                inputWin.Content = grid;

                if (inputWin.ShowDialog() == true) ActualizarUI_MesPrograma();
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
                if (fCrea.HasValue && new DateTime(fCrea.Value.Year, fCrea.Value.Month, 1) > mesReferenciaGrid) continue;

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

        private async Task CargarTerritorios(DateTime mesReferencia, DateTime fechaInicio, DateTime fechaFin, List<Persona> personasMes)
        {
            var listaPlana = DatabaseHelper.ObtenerTerritoriosPorCongregacion(_congreCodigo);
            var grupos = listaPlana.GroupBy(t => t.Nombre).ToList();

            var nombresS21Disponibles = personasMes
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
                if (fElim.HasValue && new DateTime(fElim.Value.Year, fElim.Value.Month, 1) < fechaBaseStats) continue;

                DateTime? fechaCreacion = ParsearFecha(grupo.FirstOrDefault()?.FechaCreacion);
                if (fechaCreacion.HasValue && new DateTime(fechaCreacion.Value.Year, fechaCreacion.Value.Month, 1) > fechaBaseStats) continue;

                totalTerritorios++;
                var asignaciones = grupo.ToList();
                var fila = new FilaTablaTerritorio { Nombre = grupo.Key, UltimaVez = grupo.FirstOrDefault()?.UltimaFechaCompletado };

                var asignacionesFiltradas = asignaciones.Where(a => EsFechaEnRango(a.FechaAsignado, fechaInicio, fechaFin)).ToList();
                fila.Vuelta1 = CrearCeldaVuelta(asignacionesFiltradas.ElementAtOrDefault(0), nombresS21Disponibles, cacheFuzzy);
                fila.Vuelta2 = CrearCeldaVuelta(asignacionesFiltradas.ElementAtOrDefault(1), nombresS21Disponibles, cacheFuzzy);
                fila.Vuelta3 = CrearCeldaVuelta(asignacionesFiltradas.ElementAtOrDefault(2), nombresS21Disponibles, cacheFuzzy);
                fila.Vuelta4 = CrearCeldaVuelta(asignacionesFiltradas.ElementAtOrDefault(3), nombresS21Disponibles, cacheFuzzy);

                filasGrid.Add(fila);

                bool trabajadoEn6Meses = false; bool enCurso = false; bool caducado = false;
                DateTime? ultimaDevolucionValida = null;

                foreach (var asig in asignaciones)
                {
                    DateTime? fAsig = ParsearFecha(asig.FechaAsignado);
                    DateTime? fComp = ParsearFecha(asig.FechaCompletado);

                    if (fComp.HasValue && fComp.Value >= fecha6MesesAntes && fComp.Value < fechaBaseStats) trabajadoEn6Meses = true;

                    if (fAsig.HasValue && fAsig.Value < fechaBaseStats && (!fComp.HasValue || fComp.Value >= fechaBaseStats))
                    {
                        enCurso = true;
                        if (fAsig.Value <= fecha4MesesAntes) caducado = true;
                    }

                    if (fComp.HasValue && fComp.Value < fechaBaseStats)
                    {
                        if (!ultimaDevolucionValida.HasValue || fComp.Value > ultimaDevolucionValida.Value) ultimaDevolucionValida = fComp.Value;
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
                else sinTrabajar2Anos++;
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

        private async void ExpanderMapa_Expanded(object sender, RoutedEventArgs e)
        {
            MapaWebBrowser.Visibility = Visibility.Visible;

            var expander = sender as Expander;
            if (expander != null && expander.IsExpanded)
            {
                if (_mapaCargado && !string.IsNullOrEmpty(_htmlMapaCache))
                {
                    MapaWebBrowser.NavigateToString(_htmlMapaCache);
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
            if (_cargandoDatos) return;
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

        // ==========================================
        // GESTIÓN DE S-303 Y S-2
        // ==========================================
        private void CargarS303yS2()
        {
            // 1. Cargar S-303 inteligente (Busca copia o pide plantilla)
            var listaUI = new List<DocumentoS303UI>();
            string mesRef = _mesS303.ToString("yyyy-MM");
            string identificadorVisita = $"S-303_{_congreCodigo}_{mesRef}";

            // Busca si ya existe la copia específica
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
                // Busca la plantilla genérica en "Envío de S-303"
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

                            File.Copy(docUI.DocumentoOriginal.RutaLocal, rutaCopia, true);

                            // AQUÍ ESTÁ EL CAMBIO: Llamamos a tu método original de 3 parámetros
                            DatabaseHelper.InsertarDocumento("S-303", nombreCopia, rutaCopia);

                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rutaCopia) { UseShellExecute = true });
                            ActualizarUI_MesS303(); // Recargar para que ahora salga el botón "Abrir / Editar"
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error al crear la copia del S-303: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private async void BtnAprobarRecomendacionVisita_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RecomendacionHub rec)
            {
                var ventana = new AprobarNombramientoWindow(rec);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
                    PanelCargando.Visibility = Visibility.Visible;

                    try
                    {
                        await Task.Run(() => SincronizadorJWHub.AprobarRecomendacionYEnviarCorreo(folderPath, rec, ventana.FechaSeleccionada, ventana.ArchivosPdfGenerados));

                        // Actualizamos su cargo en la BD y en su versión del mes del ministerio
                        var personasDB = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo);
                        var personaBD = personasDB.FirstOrDefault(x =>
                            (!string.IsNullOrEmpty(x.Nombre) && x.Nombre.Contains(rec.Hermano, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(x.NombreComun) && x.NombreComun.Contains(rec.Hermano, StringComparison.OrdinalIgnoreCase)));

                        if (personaBD != null && !string.IsNullOrEmpty(rec.RecomendadoComo))
                        {
                            if (rec.RecomendadoComo.Contains("Anciano", StringComparison.OrdinalIgnoreCase)) personaBD.Nombramiento = "Anciano";
                            else if (rec.RecomendadoComo.Contains("Siervo", StringComparison.OrdinalIgnoreCase)) personaBD.Nombramiento = "Siervo ministerial";

                            DatabaseHelper.ActualizarPersonaDesdeVisitas(personaBD, _mesMinisterio.ToString("yyyy-MM"));
                        }

                        // Limpieza
                        DatabaseHelper.EliminarRecomendacion(rec.Hermano, rec.Congregacion);
                        var archivosViejos = DatabaseHelper.ObtenerDocumentos("DescargaHub_" + rec.Hermano);
                        foreach (var arc in archivosViejos)
                        {
                            DatabaseHelper.EliminarDocumento(arc.Id);
                            if (File.Exists(arc.RutaLocal)) File.Delete(arc.RutaLocal);
                        }

                        MessageBox.Show($"La recomendación de {rec.Hermano} se ha aprobado con éxito.", "Aprobado", MessageBoxButton.OK, MessageBoxImage.Information);

                        CargarS303yS2();
                        ActualizarUI_MesMinisterio();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error durante la automatización: {ex.Message}", "Fallo", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        PanelCargando.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private async void BtnCancelarRecomendacionVisita_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RecomendacionHub rec)
            {
                if (MessageBox.Show($"¿Estás seguro de querer cancelar la recomendación para {rec.Hermano}?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
                    PanelCargando.Visibility = Visibility.Visible;

                    try
                    {
                        await Task.Run(() => SincronizadorJWHub.CancelarRecomendacionHub(folderPath, rec));

                        DatabaseHelper.EliminarRecomendacion(rec.Hermano, rec.Congregacion);
                        var archivosViejos = DatabaseHelper.ObtenerDocumentos("DescargaHub_" + rec.Hermano);
                        foreach (var arc in archivosViejos)
                        {
                            DatabaseHelper.EliminarDocumento(arc.Id);
                            if (File.Exists(arc.RutaLocal)) File.Delete(arc.RutaLocal);
                        }

                        MessageBox.Show($"La recomendación de {rec.Hermano} se ha cancelado.", "Cancelado", MessageBoxButton.OK, MessageBoxImage.Information);
                        CargarS303yS2();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error durante la automatización: {ex.Message}", "Fallo", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        PanelCargando.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        public class NominatimResult { public string lat { get; set; } public string lon { get; set; } }
        public class MesSelectorVisita { public DateTime FechaBase { get; set; } public string NombreVisual { get; set; } }

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

        public class DocumentoS303UI
        {
            public string NombreVisual { get; set; }
            public string TextoBoton { get; set; }
            public bool EsPlantilla { get; set; }
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