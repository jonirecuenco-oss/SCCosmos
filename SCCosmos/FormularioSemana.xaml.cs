using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public class CongregacionDisplayItem
    {
        public string Nombre { get; set; }
        public string Display { get; set; }
        public int Meses { get; set; }
        public bool EsSugerida { get; set; }
    }

    public partial class FormularioSemana : Window
    {
        private SemanaCalendario _semanaActual;
        private bool _inicializando = true;

        public FormularioSemana(SemanaCalendario semana)
        {
            InitializeComponent();
            _semanaActual = semana;

            TxtRango.Text = semana.RangoFechas;

            // =================================================================================
            // 1. CARGA DE CONGREGACIONES, CÁLCULO DE MESES Y BÚSQUEDA DE "LA SUGERIDA"
            // =================================================================================
            var congregacionesRaw = DatabaseHelper.ObtenerCongregaciones();
            var todasLasVisitas = new List<SemanaCalendario>();

            DateTime fechaBucle = new DateTime(2020, 1, 1);
            while (fechaBucle.DayOfWeek != DayOfWeek.Tuesday) fechaBucle = fechaBucle.AddDays(1);
            DateTime limite = DateTime.Today.AddDays(365 * 2);

            while (fechaBucle <= limite)
            {
                string isoId = fechaBucle.ToString("yyyy-MM-dd");
                var sems = DatabaseHelper.ObtenerTodasSemanasPorFecha(isoId);
                if (sems != null)
                {
                    foreach (var sem in sems)
                    {
                        if (!string.IsNullOrEmpty(sem.Congregacion) && sem.Tipo != null && sem.Tipo.Contains("Visita"))
                        {
                            todasLasVisitas.Add(sem);
                        }
                    }
                }
                fechaBucle = fechaBucle.AddDays(7);
            }

            string congregacionSugerida = "";
            string jsonMatriz = DatabaseHelper.ObtenerConfiguracion("MatrizCicloVisitas", "");

            if (!string.IsNullOrEmpty(jsonMatriz))
            {
                var filas = System.Text.Json.JsonSerializer.Deserialize<List<FilaCiclo>>(jsonMatriz);
                var plana = new List<string>();
                for (int col = 1; col <= 6; col++)
                {
                    foreach (var f in filas.OrderBy(x => x.Orden))
                    {
                        string v = col == 1 ? f.Visita1 : col == 2 ? f.Visita2 : col == 3 ? f.Visita3 : col == 4 ? f.Visita4 : col == 5 ? f.Visita5 : f.Visita6;
                        if (!string.IsNullOrEmpty(v)) plana.Add(v);
                    }
                }

                if (plana.Count > 0)
                {
                    // CORRECCIÓN 1: Solo contamos las visitas "Oficiales" que hacen avanzar el ciclo 
                    // (Ignoramos Pastoreo y Visitas de SCS para que el índice no se desfase)
                    int visitasPasadas = todasLasVisitas.Count(v =>
                        v.FechaMartes < semana.FechaMartes &&
                        (v.Tipo == "Visita SC" || v.Tipo == "Visita (con SCS)"));

                    int startIndex = visitasPasadas % plana.Count;

                    for (int i = 0; i < plana.Count; i++)
                    {
                        int checkIdx = (startIndex + i) % plana.Count;
                        string candidato = plana[checkIdx];

                        // CORRECCIÓN 2: Reducimos el radar a 75 días (2.5 meses). 
                        bool yaProgramadaCerca = todasLasVisitas.Any(v =>
                            v.Congregacion.Trim() == candidato &&
                            v.FechaMartes != semana.FechaMartes &&
                            Math.Abs((v.FechaMartes - semana.FechaMartes).TotalDays) < 75);

                        if (!yaProgramadaCerca)
                        {
                            congregacionSugerida = candidato;
                            break;
                        }
                    }
                }
            }

            var listaCombo = new List<CongregacionDisplayItem>();
            foreach (var c in congregacionesRaw)
            {
                var prop = c.GetType().GetProperty("Nombre");
                string nombre = prop != null ? prop.GetValue(c, null)?.ToString() : c.ToString();
                string display = nombre;
                int mesesParaOrdenar = 9999;

                var visitasAnteriores = todasLasVisitas.Where(v => v.Congregacion.Trim() == nombre && v.FechaMartes < semana.FechaMartes).ToList();
                var visitasPosteriores = todasLasVisitas.Where(v => v.Congregacion.Trim() == nombre && v.FechaMartes > semana.FechaMartes).ToList();

                var visitaFuturaCercana = visitasPosteriores.FirstOrDefault(v => (v.FechaMartes - semana.FechaMartes).TotalDays < 150);

                if (visitaFuturaCercana != null)
                {
                    int dias = (visitaFuturaCercana.FechaMartes - semana.FechaMartes).Days;
                    if (dias < 30) display = $"{nombre} (¡Ojo! Programada este mes)";
                    else if (dias < 60) display = $"{nombre} (Programada el mes que viene)";
                    else display = $"{nombre} (Programada en {dias / 30} meses)";

                    mesesParaOrdenar = -1;
                }
                else if (visitasAnteriores.Any())
                {
                    var ultima = visitasAnteriores.OrderByDescending(v => v.FechaMartes).First().FechaMartes;
                    mesesParaOrdenar = ((semana.FechaMartes.Year - ultima.Year) * 12) + semana.FechaMartes.Month - ultima.Month;

                    if (mesesParaOrdenar == 0) display = $"{nombre} (este mes)";
                    else if (mesesParaOrdenar == 1) display = $"{nombre} (hace 1 mes)";
                    else display = $"{nombre} (hace {mesesParaOrdenar} meses)";
                }
                else
                {
                    display = $"{nombre} (nunca)";
                }

                bool esSug = (nombre == congregacionSugerida);
                if (esSug) display = $"⭐ {display} (SUGERIDA)";

                listaCombo.Add(new CongregacionDisplayItem { Nombre = nombre, Display = display, Meses = mesesParaOrdenar, EsSugerida = esSug });
            }

            listaCombo = listaCombo.OrderByDescending(x => x.EsSugerida).ThenByDescending(x => x.Meses).ToList();

            ComboCongregaciones.SelectedValuePath = "Nombre";
            ComboCongregaciones.DisplayMemberPath = "Display";
            ComboCongregaciones.ItemsSource = listaCombo;

            // =================================================================================
            // CONTINÚA LA CARGA NORMAL DEL FORMULARIO
            // =================================================================================

            foreach (ComboBoxItem item in ComboActividades.Items)
            {
                if (item.Content.ToString() == semana.Actividad)
                {
                    ComboActividades.SelectedItem = item;
                    break;
                }
            }

            ComboActividades_SelectionChanged(null, null);

            string tipoInicial = string.IsNullOrEmpty(semana.Tipo) ? "Visita SC" : semana.Tipo;
            foreach (ComboBoxItem item in ComboTipo.Items)
            {
                if (item.Content.ToString() == tipoInicial)
                {
                    ComboTipo.SelectedItem = item;
                    break;
                }
            }

            ComboTipo_SelectionChanged(null, null);

            if (!string.IsNullOrEmpty(semana.Congregacion))
            {
                ComboCongregaciones.SelectedValue = semana.Congregacion;
            }

            if (!string.IsNullOrEmpty(semana.Alojamiento))
            {
                foreach (ComboBoxItem item in ComboAlojamiento.Items)
                {
                    if (item.Content.ToString() == semana.Alojamiento)
                    {
                        ComboAlojamiento.SelectedItem = item;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(semana.Acompanante))
            {
                string[] partesY = semana.Acompanante.Split(new[] { " y " }, StringSplitOptions.None);

                if (tipoInicial == "Visita (con SCS)" || tipoInicial == "Visita de SCS")
                {
                    string nombreSCS = partesY[0].Trim();
                    foreach (Persona p in ComboSCS.Items)
                    {
                        if (p.NombreJWHub == nombreSCS)
                        {
                            ComboSCS.SelectedItem = p;
                            break;
                        }
                    }
                    if (partesY.Length > 1)
                    {
                        string nombreEsp = partesY[1].Trim();
                        foreach (Persona p in ComboEsposaSCS.Items)
                        {
                            if (p.NombreJWHub == nombreEsp)
                            {
                                ComboEsposaSCS.SelectedItem = p;
                                break;
                            }
                        }
                    }
                }
                else if (tipoInicial == "Visita (de pastoreo)")
                {
                    string maridoFull = partesY[0].Trim();

                    if (maridoFull.Contains(","))
                    {
                        var partesComa = maridoFull.Split(',');
                        TxtApellidoPastoreo.Text = partesComa[0].Trim();
                        TxtHermanoPastoreo.Text = partesComa[1].Trim();
                    }
                    else
                    {
                        TxtHermanoPastoreo.Text = maridoFull;
                        TxtApellidoPastoreo.Text = "";
                    }

                    if (partesY.Length > 1)
                    {
                        TxtEsposaPastoreo.Text = partesY[1].Trim();
                    }
                }
            }

            ComboSeccionesAsamblea.ItemsSource = DatabaseHelper.ObtenerSecciones();
            if (!string.IsNullOrEmpty(semana.SeccionAsamblea))
            {
                ComboSeccionesAsamblea.SelectedValue = semana.SeccionAsamblea;
            }

            DateTime sabado = semana.FechaMartes.AddDays(4);
            DateTime domingo = semana.FechaMartes.AddDays(5);

            string txtSabado = char.ToUpper(sabado.ToString("dddd")[0]) + sabado.ToString("dddd").Substring(1) + " " + sabado.Day + " de " + sabado.ToString("MMMM");
            string txtDomingo = char.ToUpper(domingo.ToString("dddd")[0]) + domingo.ToString("dddd").Substring(1) + " " + domingo.Day + " de " + domingo.ToString("MMMM");

            ComboDiaAsamblea.Items.Clear();
            ComboDiaAsamblea.Items.Add(new ComboBoxItem { Content = txtSabado });
            ComboDiaAsamblea.Items.Add(new ComboBoxItem { Content = txtDomingo });

            if (!string.IsNullOrEmpty(semana.FechaAsamblea))
            {
                foreach (ComboBoxItem item in ComboDiaAsamblea.Items)
                {
                    if (item.Content.ToString() == semana.FechaAsamblea)
                    {
                        ComboDiaAsamblea.SelectedItem = item;
                        break;
                    }
                }
            }

            _inicializando = false;
        }

        private void ComboActividades_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboActividades.SelectedItem == null) return;
            string actividad = ((ComboBoxItem)ComboActividades.SelectedItem).Content.ToString();

            if (actividad == "Visita")
            {
                ComboCongregaciones.IsEnabled = true;
                PanelDetallesVisita.Visibility = Visibility.Visible;
                if (PanelAsamblea != null) PanelAsamblea.Visibility = Visibility.Collapsed;
            }
            else if (actividad == "Asamblea de circuito")
            {
                ComboCongregaciones.IsEnabled = false;
                ComboCongregaciones.SelectedItem = null;
                PanelDetallesVisita.Visibility = Visibility.Collapsed;
                if (PanelAsamblea != null) PanelAsamblea.Visibility = Visibility.Visible;
            }
            else if (actividad == "Conmemoración")
            {
                ComboCongregaciones.IsEnabled = false;
                ComboCongregaciones.SelectedItem = null;
                PanelDetallesVisita.Visibility = Visibility.Collapsed;
                if (PanelAsamblea != null) PanelAsamblea.Visibility = Visibility.Collapsed;
            }
            else
            {
                ComboCongregaciones.IsEnabled = false;
                ComboCongregaciones.SelectedItem = null;
                PanelDetallesVisita.Visibility = Visibility.Collapsed;
                if (PanelAsamblea != null) PanelAsamblea.Visibility = Visibility.Collapsed;
            }
        }

        private void ComboTipo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboTipo.SelectedItem == null || ComboAlojamiento == null) return;
            string tipo = ((ComboBoxItem)ComboTipo.SelectedItem).Content.ToString();

            ComboAlojamiento.Items.Clear();
            ComboAlojamiento.IsEnabled = true;

            if (tipo == "Visita SC")
            {
                PanelSCS.Visibility = Visibility.Collapsed;
                PanelPastoreo.Visibility = Visibility.Collapsed;

                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Con alojamiento" });
                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Sin alojamiento" });
            }
            else if (tipo == "Visita (con SCS)")
            {
                PanelSCS.Visibility = Visibility.Visible;
                PanelPastoreo.Visibility = Visibility.Collapsed;

                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Con alojamiento (ambos)" });
                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Con alojamiento (solo yo)" });
                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Con alojamiento (solo acompañante)" });
                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Sin alojamiento (ambos)" });

                CargarListaSCS();
            }
            else if (tipo == "Visita de SCS")
            {
                PanelSCS.Visibility = Visibility.Visible;
                PanelPastoreo.Visibility = Visibility.Collapsed;

                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "" });
                ComboAlojamiento.IsEnabled = false;

                CargarListaSCS();
            }
            else if (tipo == "Visita (de pastoreo)")
            {
                PanelSCS.Visibility = Visibility.Collapsed;
                PanelPastoreo.Visibility = Visibility.Visible;

                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Con alojamiento (ambos)" });
                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Con alojamiento (solo yo)" });
                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Con alojamiento (solo acompañante)" });
                ComboAlojamiento.Items.Add(new ComboBoxItem { Content = "Sin alojamiento (ambos)" });
            }

            if (!_inicializando && ComboAlojamiento.Items.Count > 0)
            {
                ComboAlojamiento.SelectedIndex = 0;
            }
        }

        private void CargarListaSCS()
        {
            var todasLasPersonas = DatabaseHelper.ObtenerPersonas();
            var listaSCS = todasLasPersonas
                .Where(p => !string.IsNullOrEmpty(p.Privilegios) && p.Privilegios.Contains("Superintendente de circuito sustituto"))
                .OrderBy(p => p.NombreJWHub)
                .ToList();

            ComboSCS.ItemsSource = listaSCS;
        }

        private void ComboSCS_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboSCS.SelectedItem is Persona scsSeleccionado)
            {
                var todasLasPersonas = DatabaseHelper.ObtenerPersonas();
                var posiblesEsposas = todasLasPersonas
                    .Where(p => p.CongregacionCodigo == scsSeleccionado.CongregacionCodigo && p.Id != scsSeleccionado.Id)
                    .OrderBy(p => p.NombreJWHub)
                    .ToList();

                ComboEsposaSCS.ItemsSource = posiblesEsposas;
            }
            else
            {
                ComboEsposaSCS.ItemsSource = null;
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            var actItem = ComboActividades.SelectedItem as ComboBoxItem;
            _semanaActual.Actividad = actItem != null ? actItem.Content.ToString() : "";

            if (_semanaActual.Actividad == "Visita")
            {
                _semanaActual.Congregacion = ComboCongregaciones.SelectedValue != null ? ComboCongregaciones.SelectedValue.ToString() : "";

                var tipoItem = ComboTipo.SelectedItem as ComboBoxItem;
                _semanaActual.Tipo = tipoItem != null ? tipoItem.Content.ToString() : "Visita SC";

                var alojItem = ComboAlojamiento.SelectedItem as ComboBoxItem;
                _semanaActual.Alojamiento = alojItem != null ? alojItem.Content.ToString() : "";

                if (_semanaActual.Tipo == "Visita SC")
                {
                    _semanaActual.Acompanante = "";
                }
                else if (_semanaActual.Tipo == "Visita (con SCS)" || _semanaActual.Tipo == "Visita de SCS")
                {
                    var scs = ComboSCS.SelectedItem as Persona;
                    var necesariaEsposa = ComboEsposaSCS.SelectedItem as Persona;

                    string nombreSCS = scs != null ? scs.NombreJWHub : "";
                    string nombreEsposa = necesariaEsposa != null ? necesariaEsposa.NombreJWHub : "";

                    if (string.IsNullOrEmpty(nombreEsposa))
                        _semanaActual.Acompanante = nombreSCS;
                    else
                        _semanaActual.Acompanante = $"{nombreSCS} y {nombreEsposa}";
                }
                else if (_semanaActual.Tipo == "Visita (de pastoreo)")
                {
                    string hermanoNombre = TxtHermanoPastoreo.Text.Trim();
                    string hermanoApellido = TxtApellidoPastoreo.Text.Trim();
                    string esposa = TxtEsposaPastoreo.Text.Trim();

                    string maridoFormateado = !string.IsNullOrEmpty(hermanoApellido)
                        ? $"{hermanoApellido}, {hermanoNombre}"
                        : hermanoNombre;

                    if (string.IsNullOrEmpty(esposa))
                        _semanaActual.Acompanante = maridoFormateado;
                    else
                        _semanaActual.Acompanante = $"{maridoFormateado} y {esposa}";
                }

                _semanaActual.SeccionAsamblea = "";
                _semanaActual.FechaAsamblea = "";
            }
            else if (_semanaActual.Actividad == "Asamblea de circuito")
            {
                _semanaActual.SeccionAsamblea = ComboSeccionesAsamblea.SelectedValue?.ToString() ?? "";

                var asambleaDiaItem = ComboDiaAsamblea.SelectedItem as ComboBoxItem;
                _semanaActual.FechaAsamblea = asambleaDiaItem != null ? asambleaDiaItem.Content.ToString() : "";

                _semanaActual.Congregacion = "";
                _semanaActual.Tipo = "";
                _semanaActual.Alojamiento = "";
                _semanaActual.Acompanante = "";
            }
            else
            {
                _semanaActual.Congregacion = "";
                _semanaActual.Tipo = "";
                _semanaActual.Alojamiento = "";
                _semanaActual.Acompanante = "";
                _semanaActual.SeccionAsamblea = "";
                _semanaActual.FechaAsamblea = "";
            }

            this.DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}