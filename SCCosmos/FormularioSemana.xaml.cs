using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class FormularioSemana : Window
    {
        private SemanaCalendario _semanaActual;
        private bool _inicializando = true;

        public FormularioSemana(SemanaCalendario semana)
        {
            InitializeComponent();
            _semanaActual = semana;

            TxtRango.Text = semana.RangoFechas;

            // 1. Cargar las Congregaciones de la Base de Datos
            var congregaciones = DatabaseHelper.ObtenerCongregaciones();
            ComboCongregaciones.ItemsSource = congregaciones;

            // 2. Seleccionar Actividad Actual
            foreach (ComboBoxItem item in ComboActividades.Items)
            {
                if (item.Content.ToString() == semana.Actividad)
                {
                    ComboActividades.SelectedItem = item;
                    break;
                }
            }

            // 3. Seleccionar Congregación
            if (!string.IsNullOrEmpty(semana.Congregacion))
            {
                ComboCongregaciones.SelectedValue = semana.Congregacion;
            }

            // 4. Seleccionar Tipo (Si está vacío, por defecto "Visita SC")
            string tipoInicial = string.IsNullOrEmpty(semana.Tipo) ? "Visita SC" : semana.Tipo;
            foreach (ComboBoxItem item in ComboTipo.Items)
            {
                if (item.Content.ToString() == tipoInicial)
                {
                    ComboTipo.SelectedItem = item;
                    break;
                }
            }

            // 5. Seleccionar Alojamiento
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

            // 6. Recuperar acompañantes previamente guardados
            if (!string.IsNullOrEmpty(semana.Acompanante))
            {
                string[] partesY = semana.Acompanante.Split(new[] { " y " }, StringSplitOptions.None);

                if (tipoInicial == "Visita (con SCS)")
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

            // 7. Cargar secciones de asamblea
            ComboSeccionesAsamblea.ItemsSource = DatabaseHelper.ObtenerSecciones();
            if (!string.IsNullOrEmpty(semana.SeccionAsamblea))
            {
                ComboSeccionesAsamblea.SelectedValue = semana.SeccionAsamblea;
            }

            // ======================================================================
            // 8. CÁLCULO INTELIGENTE DEL FIN DE SEMANA PARA LA ASAMBLEA
            // ======================================================================
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

            // ESTA ES LA CLAVE: Forzamos a que el diseño se actualice ahora que ya hemos cargado todo
            ComboActividades_SelectionChanged(null, null);
            ComboTipo_SelectionChanged(null, null);
        }

        private void ComboActividades_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboActividades.SelectedItem == null || _inicializando) return;
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
                else if (_semanaActual.Tipo == "Visita (con SCS)")
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