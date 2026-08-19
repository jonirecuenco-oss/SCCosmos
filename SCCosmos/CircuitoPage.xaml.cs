using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SCCosmos
{
    public partial class CircuitoPage : Page
    {
        public ObservableCollection<Congregacion> ListaCongregaciones { get; set; }
        public ObservableCollection<SeccionCircuito> ListaSecciones { get; set; }

        public CircuitoPage()
        {
            InitializeComponent();
            CargarDatos();
        }

        private void CargarDatos()
        {
            ListaSecciones = new ObservableCollection<SeccionCircuito>(DatabaseHelper.ObtenerSecciones());
            TablaSecciones.ItemsSource = ListaSecciones;

            ListaCongregaciones = new ObservableCollection<Congregacion>(DatabaseHelper.ObtenerCongregaciones());
            TablaCongregaciones.ItemsSource = ListaCongregaciones;

            var nombresSeccion = ListaSecciones.Select(s => s.Nombre).ToList();
            nombresSeccion.Insert(0, "Sin asignar");
            ColSeccionGrid.ItemsSource = nombresSeccion;
        }

        // ==========================================
        // TRADUCTORES DE FORMATO Y ESCAPE
        // ==========================================
        private string TraducirDia(string diaIngles)
        {
            switch (diaIngles.Trim().ToUpper())
            {
                case "MON": return "Lunes";
                case "TUE": return "Martes";
                case "WED": return "Miércoles";
                case "THU": return "Jueves";
                case "FRI": return "Viernes";
                case "SAT": return "Sábado";
                case "SUN": return "Domingo";
                default: return diaIngles;
            }
        }

        private string ConvertirHora24(string hora12)
        {
            string limpia = hora12.Trim().ToUpper().Replace("  ", " ");
            if (DateTime.TryParseExact(limpia, new[] { "h:mm tt", "hh:mm tt", "H:mm tt", "HH:mm tt" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
            {
                return dt.ToString("HH:mm");
            }
            if (DateTime.TryParse(limpia, out DateTime dt2))
            {
                return dt2.ToString("HH:mm");
            }
            return hora12.Trim();
        }

        private string EscapeJsString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("'", "\\'")
                    .Replace("\r", "")
                    .Replace("\n", "")
                    .Replace("\u2028", "")
                    .Replace("\u2029", "");
        }

        // ==========================================
        // LÓGICA DE SECCIONES
        // ==========================================
        private void BtnCrearSeccion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtLetraSeccion.Text) || string.IsNullOrWhiteSpace(TxtLugarAsamblea.Text))
            {
                MessageBox.Show("Debes rellenar la letra de la sección y el lugar de asamblea.");
                return;
            }

            DatabaseHelper.GuardarSeccion(new SeccionCircuito
            {
                Nombre = TxtLetraSeccion.Text.Trim().ToUpper(),
                LugarAsamblea = TxtLugarAsamblea.Text.Trim()
            });

            TxtLetraSeccion.Clear();
            TxtLugarAsamblea.Clear();
            CargarDatos();
        }

        private void BtnBorrarSeccion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            int idSeccion = (int)boton.Tag;

            if (MessageBox.Show("¿Seguro que deseas eliminar esta sección?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                DatabaseHelper.EliminarSeccion(idSeccion);
                CargarDatos();
            }
        }

        // ==========================================
        // LÓGICA DE CONGREGACIONES Y EDICIÓN EN VIVO
        // ==========================================
        private void BtnAnadirCongregacion_Click(object sender, RoutedEventArgs e)
        {
            FormularioCongregacion ventanaForm = new FormularioCongregacion();
            ventanaForm.Owner = Window.GetWindow(this);

            if (ventanaForm.ShowDialog() == true)
            {
                foreach (var congre in ListaCongregaciones)
                {
                    if (congre.Codigo.Trim() == ventanaForm.CongregacionResult.Codigo)
                    {
                        MessageBox.Show("Ese código de congregación ya existe.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                DatabaseHelper.InsertarCongregacion(ventanaForm.CongregacionResult);
                CargarDatos();
            }
        }

        private void BtnEditarCongregacion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var congreSeleccionada = boton.Tag as Congregacion;

            if (congreSeleccionada != null)
            {
                FormularioCongregacion ventanaForm = new FormularioCongregacion(congreSeleccionada);
                ventanaForm.Owner = Window.GetWindow(this);

                if (ventanaForm.ShowDialog() == true)
                {
                    DatabaseHelper.ActualizarCongregacion(congreSeleccionada);
                    CargarDatos();
                }
            }
        }

        private void BtnBorrarCongregacion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var congreSeleccionada = boton.Tag as Congregacion;

            if (congreSeleccionada != null)
            {
                var resultado = MessageBox.Show($"¿Estás seguro de eliminar '{congreSeleccionada.Nombre}'?", "Confirmar baja", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (resultado == MessageBoxResult.Yes)
                {
                    DatabaseHelper.EliminarCongregacion(congreSeleccionada.Codigo);
                    ListaCongregaciones.Remove(congreSeleccionada);
                }
            }
        }

        private void TablaCongregaciones_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Congregacion cong)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DatabaseHelper.ActualizarCongregacion(cong);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // ==========================================
        // LÓGICA DEL MAPA
        // ==========================================
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tc && tc.SelectedIndex == 1) // El Mapa ahora es el índice 1
            {
                DibujarMapa();
            }
        }

        private void DibujarMapa()
        {
            var sb = new System.Text.StringBuilder();
            var elementosKml = DatabaseHelper.ObtenerElementosMapa();

            foreach (var elem in elementosKml)
            {
                string nombreJS = EscapeJsString(elem.Nombre);
                // Si la descripción original lleva <br>, la conservamos. Si son saltos de línea crudos, los convertimos para HTML
                string descHTML = EscapeJsString(elem.Descripcion.Replace("\n", "<br>"));

                string popupHtml = $"<div style=\\'max-height:250px; overflow-y:auto; font-family:Segoe UI;\\'><h3 style=\\'margin-top:0px; margin-bottom:5px; color:#1e1e1e;\\'>{nombreJS}</h3>{descHTML}</div>";

                if (elem.Tipo == "Point")
                {
                    sb.AppendLine($"var marker = L.marker({elem.Coordenadas}).addTo(map).bindPopup('{popupHtml}');");
                    sb.AppendLine($"markers.push(marker);");
                }
                else if (elem.Tipo == "Polygon")
                {
                    sb.AppendLine($"var poly = L.polygon({elem.Coordenadas}, {{color: '#4BA3E3', weight: 2, fillOpacity: 0.15}}).addTo(map).bindPopup('{popupHtml}');");
                    sb.AppendLine($"polygons.push(poly);");
                }
            }

            string html = $@"
            <!DOCTYPE html>
            <html><head>
                <meta http-equiv='X-UA-Compatible' content='IE=edge' />
                <meta charset='UTF-8'>
                <link rel='stylesheet' href='https://unpkg.com/leaflet@1.7.1/dist/leaflet.css' />
                <script src='https://unpkg.com/leaflet@1.7.1/dist/leaflet.js'></script>
                <style>body{{margin:0;padding:0;font-family:Segoe UI, sans-serif;}} #map{{height:100vh;background:#252526;}}</style>
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

            MapaWebBrowser.NavigateToString(html);
        }

        // ==========================================
        // EXTRACTOR AUTOMÁTICO DEL KMZ
        // ==========================================
        private void BtnSubirKMZ_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Title = "Seleccionar Mapa KMZ", Filter = "Archivos KMZ o KML (*.kmz;*.kml)|*.kmz;*.kml" };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string contenidoKml = "";

                    if (ofd.FileName.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var archive = ZipFile.OpenRead(ofd.FileName))
                        {
                            var entradaKml = archive.Entries.FirstOrDefault(ent => ent.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
                            if (entradaKml != null)
                            {
                                using (var stream = entradaKml.Open())
                                using (var reader = new StreamReader(stream))
                                {
                                    contenidoKml = reader.ReadToEnd();
                                }
                            }
                        }
                    }
                    else
                    {
                        contenidoKml = File.ReadAllText(ofd.FileName);
                    }

                    if (string.IsNullOrEmpty(contenidoKml))
                    {
                        MessageBox.Show("No se encontró información en el archivo KMZ.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Limpiamos los mapas viejos para cargar los nuevos
                    DatabaseHelper.LimpiarElementosMapa();

                    XDocument doc = XDocument.Parse(contenidoKml);
                    XNamespace ns = doc.Root.GetDefaultNamespace();
                    var placemarks = doc.Descendants(ns + "Placemark");

                    int elementosGeograficos = 0;
                    int salonesActualizados = 0;
                    int territoriosActualizados = 0;

                    foreach (var pm in placemarks)
                    {
                        string nombrePm = pm.Element(ns + "name")?.Value?.Trim() ?? "";
                        string descOriginal = pm.Element(ns + "description")?.Value?.Trim() ?? "";

                        // PARTE A: ALMACENAR LA GEOMETRÍA PARA EL MAPA
                        foreach (var coordNode in pm.Descendants(ns + "coordinates"))
                        {
                            string coordsRaw = coordNode.Value;
                            var pairs = coordsRaw.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                            List<string> latlons = new List<string>();
                            foreach (var p in pairs)
                            {
                                var parts = p.Split(',');
                                if (parts.Length >= 2)
                                {
                                    string lon = parts[0].Trim();
                                    string lat = parts[1].Trim();
                                    latlons.Add($"[{lat.Replace(",", ".")}, {lon.Replace(",", ".")}]");
                                }
                            }

                            if (latlons.Count > 0)
                            {
                                string tipo = latlons.Count == 1 ? "Point" : "Polygon";
                                string jsCoords = latlons.Count == 1 ? latlons[0] : "[" + string.Join(", ", latlons) + "]";

                                DatabaseHelper.InsertarElementoMapa(new ElementoMapa
                                {
                                    Nombre = nombrePm,
                                    Tipo = tipo,
                                    Coordenadas = jsCoords,
                                    Descripcion = descOriginal
                                });
                                elementosGeograficos++;
                            }
                        }

                        // PARTE B: EXTRACCIÓN DE DATOS PARA LA TABLA DE CONGREGACIONES
                        string descLimpia = Regex.Replace(descOriginal, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
                        descLimpia = Regex.Replace(descLimpia, @"<[^>]+>", "");

                        double ptoLat = 0, ptoLon = 0;
                        var punto = pm.Descendants(ns + "Point").FirstOrDefault()?.Element(ns + "coordinates")?.Value?.Trim();
                        if (!string.IsNullOrEmpty(punto))
                        {
                            var tPunto = punto.Split(',');
                            if (tPunto.Length >= 2) { double.TryParse(tPunto[0], NumberStyles.Any, CultureInfo.InvariantCulture, out ptoLon); double.TryParse(tPunto[1], NumberStyles.Any, CultureInfo.InvariantCulture, out ptoLat); }
                        }

                        // 1. ¿ES UN SALÓN DEL REINO?
                        if (descLimpia.Contains("Seating:"))
                        {
                            string direccion = "";
                            int capacidad = 0;

                            var dirMatch = Regex.Match(descLimpia, @"^(.*?)(?=\nType:|\r\nType:)", RegexOptions.Singleline);
                            if (dirMatch.Success) direccion = dirMatch.Groups[1].Value.Trim().Replace("\n", ", ").Replace("\r", "");

                            var seatMatch = Regex.Match(descLimpia, @"Seating:\s*(\d+)");
                            if (seatMatch.Success) int.TryParse(seatMatch.Groups[1].Value, out capacidad);

                            string[] lineas = descLimpia.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                            string congreEnCurso = "";
                            string diaFds = "", horaFds = "", diaSemana = "", horaSemana = "";

                            for (int i = 0; i < lineas.Length; i++)
                            {
                                if (lineas[i].StartsWith("Formed Date:") && i > 0)
                                {
                                    string nombreBruto = lineas[i - 1].Trim();
                                    congreEnCurso = Regex.Replace(nombreBruto, @"\s*\([A-Z]+\)$", "").Trim();
                                    diaFds = ""; horaFds = ""; diaSemana = ""; horaSemana = "";
                                }

                                if (!string.IsNullOrEmpty(congreEnCurso))
                                {
                                    if (lineas[i].StartsWith("Weekend Meeting:"))
                                    {
                                        var match = Regex.Match(lineas[i], @"Weekend Meeting:\s*([A-Z]{3})\s+([^-]+)");
                                        if (match.Success)
                                        {
                                            diaFds = TraducirDia(match.Groups[1].Value);
                                            horaFds = ConvertirHora24(match.Groups[2].Value);
                                        }
                                    }
                                    else if (lineas[i].StartsWith("Midweek Meeting:"))
                                    {
                                        var match = Regex.Match(lineas[i], @"Midweek Meeting:\s*([A-Z]{3})\s+([^-]+)");
                                        if (match.Success)
                                        {
                                            diaSemana = TraducirDia(match.Groups[1].Value);
                                            horaSemana = ConvertirHora24(match.Groups[2].Value);
                                        }
                                    }
                                    else if (lineas[i].StartsWith("Pubs:"))
                                    {
                                        var congreBD = ListaCongregaciones.FirstOrDefault(c =>
                                            c.Nombre.IndexOf(congreEnCurso, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            congreEnCurso.IndexOf(c.Nombre, StringComparison.OrdinalIgnoreCase) >= 0);

                                        if (congreBD != null)
                                        {
                                            DatabaseHelper.ActualizarDatosSalonKMZ(congreBD.Codigo, direccion, ptoLat, ptoLon, capacidad, diaSemana, horaSemana, diaFds, horaFds);
                                            salonesActualizados++;
                                        }
                                        congreEnCurso = "";
                                    }
                                }
                            }
                        }
                        // 2. ¿ES UN MAPA DE TERRITORIO?
                        else if (descLimpia.Contains("Population:"))
                        {
                            int poblacion = 0;
                            var popMatch = Regex.Match(descLimpia, @"Population:\s*([\d\.,]+)");
                            if (popMatch.Success)
                            {
                                string popStr = popMatch.Groups[1].Value.Replace(".", "").Replace(",", "");
                                int.TryParse(popStr, out poblacion);
                            }

                            var congreBD = ListaCongregaciones.FirstOrDefault(c =>
                                c.Nombre.IndexOf(nombrePm, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                nombrePm.IndexOf(c.Nombre, StringComparison.OrdinalIgnoreCase) >= 0);

                            if (congreBD != null && poblacion > 0)
                            {
                                DatabaseHelper.ActualizarPoblacionKMZ(congreBD.Codigo, poblacion);
                                territoriosActualizados++;
                            }
                        }
                    }

                    MessageBox.Show($"¡Mapeo Inteligente Completado!\n\nSe han extraído:\n- {elementosGeograficos} zonas cartográficas y SR.\n- {salonesActualizados} horarios de congregaciones.\n- {territoriosActualizados} cifras de población.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    CargarDatos();
                    DibujarMapa();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"No se ha podido procesar el archivo KMZ:\n\n{ex.Message}", "Fallo Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}