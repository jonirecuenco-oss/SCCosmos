using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Threading;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace SCCosmos
{
    public partial class PersonasPage : Page
    {
        // =========================================================
        // VARIABLES GLOBALES (Para recibir órdenes desde InicioPage)
        // =========================================================
        public static string FiltroPendienteServicio = "";
        public static string FiltroPendienteNombramiento = "";
        public static string FiltroPendienteSolicitud = "";

        private List<Persona> _todasLasPersonas = new List<Persona>();
        private List<Persona> _personasFiltradas = new List<Persona>();

        private int _paginaActual = 1;
        private int _tamanoPagina = 15;
        private int _totalPaginas = 1;

        private string _columnaOrdenActual = "Nombre";
        private ListSortDirection _direccionOrdenActual = ListSortDirection.Ascending;

        private DispatcherTimer _timerBusqueda;
        private bool _cargandoColumnas = true;

        public PersonasPage()
        {
            InitializeComponent();
            ComprobarPerfilFirefox();

            LstServicio.ItemsSource = new List<string> { "Precursor regular", "Precursor especial", "Misionero" };
            LstSols.ItemsSource = new List<string> { "A-2", "A-19", "A-8", "G-8" };
            LstPrivs.ItemsSource = new List<string> {
                "Instructor de la Escuela del Servicio de Precursor", "Superintendente de circuito sustituto",
                "Miembro del CEH", "Superintendente de asamblea", "Superintendente auxiliar de asamblea",
                "Coordinador", "Secretario", "Miembro del GVP", "Persona que pertenece a una entidad del LDC"
            };

            CmbFiltroNombramiento.SelectedIndex = 0;
            CmbFiltroSexo.SelectedIndex = 0;
            CmbFiltroEsperanza.SelectedIndex = 0;

            _timerBusqueda = new DispatcherTimer();
            _timerBusqueda.Interval = TimeSpan.FromMilliseconds(400);
            _timerBusqueda.Tick += TimerBusqueda_Tick;

            CargarConfiguracionColumnas();

            // Nos suscribimos a los eventos de carga y descarga de la página
            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
            SincronizadorJWHub.ProgresoCambiado += Sincronizador_ProgresoCambiado;
        }

        // =========================================================================================
        // CONTROL GLOBAL DE EVENTOS (SINCRONIZACIÓN)
        // =========================================================================================
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Vinculamos el evento de sincronización
            SincronizadorJWHub.SincronizacionCambiada += Sincronizador_EstadoCambiado;
            ActualizarUI_Sincronizacion(SincronizadorJWHub.EstaSincronizando);

            // 2. Antes de cargar datos, aplicamos los filtros que vengan de la página de Inicio
            ProcesarFiltrosPendientes();

            // 3. Cargamos datos (y al terminar se aplicarán los filtros visuales)
            await CargarListaPersonasAsync();
            CargarNombramientosAsync();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            SincronizadorJWHub.SincronizacionCambiada -= Sincronizador_EstadoCambiado;
        }

        private void Sincronizador_EstadoCambiado(bool ejecutando)
        {
            Application.Current.Dispatcher.Invoke(() => ActualizarUI_Sincronizacion(ejecutando));
        }

        private void Sincronizador_ProgresoCambiado(int actual, int total)
        {
            // Como esto viene de otro hilo, usamos el Dispatcher para actualizar la interfaz
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (total > 0)
                {
                    int porcentaje = (int)Math.Round(((double)actual / total) * 100);

                    // Suponiendo que tu botón se llame BtnSincronizar:
                    BtnSincronizarHub.Content = $"Sincronizando... {porcentaje}%";
                }
            });
        }

        private void ActualizarUI_Sincronizacion(bool ejecutando)
        {
            if (ejecutando)
            {
                BtnSincronizarHub.IsEnabled = false;
                BtnSincronizarHub.Content = "Sincronizando...";
            }
            else
            {
                BtnSincronizarHub.IsEnabled = true;
                BtnSincronizarHub.Content = "Sincronizar JW Hub";
                //SpinnerCargando.Visibility = Visibility.Collapsed;
            }
        }

        // =========================================================================================
        // NUEVA FUNCIÓN: PROCESAR FILTROS QUE LLEGAN DESDE LA PÁGINA INICIO
        // =========================================================================================
        private void ProcesarFiltrosPendientes()
        {
            bool hayFiltros = false;

            if (!string.IsNullOrEmpty(FiltroPendienteServicio))
            {
                LstServicio.SelectedItems.Clear();
                var item = LstServicio.Items.Cast<string>().FirstOrDefault(x => x == FiltroPendienteServicio);
                if (item != null) LstServicio.SelectedItems.Add(item);
                FiltroPendienteServicio = "";
                hayFiltros = true;
            }

            if (!string.IsNullOrEmpty(FiltroPendienteNombramiento))
            {
                var comboItem = CmbFiltroNombramiento.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag != null && i.Tag.ToString() == FiltroPendienteNombramiento);
                if (comboItem != null) CmbFiltroNombramiento.SelectedItem = comboItem;
                FiltroPendienteNombramiento = "";
                hayFiltros = true;
            }

            if (!string.IsNullOrEmpty(FiltroPendienteSolicitud))
            {
                LstSols.SelectedItems.Clear();
                var item = LstSols.Items.Cast<string>().FirstOrDefault(x => x == FiltroPendienteSolicitud);
                if (item != null) LstSols.SelectedItems.Add(item);
                FiltroPendienteSolicitud = "";
                hayFiltros = true;
            }

            if (hayFiltros)
            {
                // Limpiamos todo el resto de opciones para que muestre de forma purista solo lo que se clicó
                TxtBuscar.Text = "";
                LstCongre.SelectedItems.Clear();
                LstPrivs.SelectedItems.Clear();
                CmbFiltroSexo.SelectedIndex = 0;
                CmbFiltroEsperanza.SelectedIndex = 0;

                // Actualizamos los textos de los botones desplegables
                BtnFiltroServicio.Content = LstServicio.SelectedItems.Count > 0 ? $"{LstServicio.SelectedItems.Count} selec." : "Todos ▼";
                BtnFiltroSols.Content = LstSols.SelectedItems.Count > 0 ? $"{LstSols.SelectedItems.Count} selec." : "Todas ▼";
                BtnFiltroCongre.Content = "Todas ▼";
                BtnFiltroPrivs.Content = "Todos ▼";
            }
        }


        private void ComprobarPerfilFirefox()
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
            if (Directory.Exists(folderPath))
            {
                if (BtnSincronizarHub != null) BtnSincronizarHub.Visibility = Visibility.Visible;
            }
        }

        private void BtnColumnas_Click(object sender, RoutedEventArgs e) { PopupColumnas.IsOpen = true; }

        private void CargarConfiguracionColumnas()
        {
            _cargandoColumnas = true;

            string config = DatabaseHelper.ObtenerConfiguracion("ColumnasPersonasVisibles", "1,0,0,0,0,1,1,1,1");
            string[] valores = config.Split(',');

            if (valores.Length == 9)
            {
                ChkColCongregacion.IsChecked = valores[0] == "1";
                ChkColFechaNacimiento.IsChecked = valores[1] == "1";
                ChkColFechaBautismo.IsChecked = valores[2] == "1";
                ChkColSexo.IsChecked = valores[3] == "1";
                ChkColEsperanza.IsChecked = valores[4] == "1";
                ChkColNombramiento.IsChecked = valores[5] == "1";
                ChkColServicio.IsChecked = valores[6] == "1";
                ChkColSolicitudes.IsChecked = valores[7] == "1";
                ChkColPrivilegios.IsChecked = valores[8] == "1";
            }

            AplicarVisibilidadColumnas();
            _cargandoColumnas = false;
        }

        private void ChkCol_Changed(object sender, RoutedEventArgs e)
        {
            if (_cargandoColumnas) return;
            AplicarVisibilidadColumnas();

            string config = $"{(ChkColCongregacion.IsChecked == true ? "1" : "0")}," +
                            $"{(ChkColFechaNacimiento.IsChecked == true ? "1" : "0")}," +
                            $"{(ChkColFechaBautismo.IsChecked == true ? "1" : "0")}," +
                            $"{(ChkColSexo.IsChecked == true ? "1" : "0")}," +
                            $"{(ChkColEsperanza.IsChecked == true ? "1" : "0")}," +
                            $"{(ChkColNombramiento.IsChecked == true ? "1" : "0")}," +
                            $"{(ChkColServicio.IsChecked == true ? "1" : "0")}," +
                            $"{(ChkColSolicitudes.IsChecked == true ? "1" : "0")}," +
                            $"{(ChkColPrivilegios.IsChecked == true ? "1" : "0")}";

            DatabaseHelper.GuardarConfiguracion("ColumnasPersonasVisibles", config);
        }

        private void AplicarVisibilidadColumnas()
        {
            ColCongregacion.Visibility = ChkColCongregacion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ColFechaNacimiento.Visibility = ChkColFechaNacimiento.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ColFechaBautismo.Visibility = ChkColFechaBautismo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ColSexo.Visibility = ChkColSexo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ColEsperanza.Visibility = ChkColEsperanza.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ColNombramiento.Visibility = ChkColNombramiento.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ColServicio.Visibility = ChkColServicio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ColSolicitudes.Visibility = ChkColSolicitudes.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ColPrivilegios.Visibility = ChkColPrivilegios.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task CargarListaPersonasAsync()
        {
            //SpinnerCargando.Visibility = Visibility.Visible;
            TablaPersonas.ItemsSource = null;

            _todasLasPersonas = await Task.Run(() => DatabaseHelper.ObtenerPersonas());

            foreach (var p in _todasLasPersonas)
            {
                if (!string.IsNullOrWhiteSpace(p.FechaNacimiento) && DateTime.TryParse(p.FechaNacimiento, out DateTime fn))
                    p.FechaNacimiento = fn.ToString("dd/MM/yyyy");

                if (!string.IsNullOrWhiteSpace(p.FechaBautismo) && DateTime.TryParse(p.FechaBautismo, out DateTime fb))
                    p.FechaBautismo = fb.ToString("dd/MM/yyyy");
            }

            var congregaciones = _todasLasPersonas.Select(p => p.CongregacionNombre).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            LstCongre.ItemsSource = congregaciones;

            AplicarFiltroYPaginar();

            //if (!SincronizadorJWHub.EstaSincronizando)
                //SpinnerCargando.Visibility = Visibility.Collapsed;
        }

        private void CargarNombramientosAsync()
        {
            var recomendaciones = DatabaseHelper.ObtenerRecomendaciones();

            if (recomendaciones.Count > 0)
            {
                foreach (var rec in recomendaciones)
                {
                    rec.ArchivosAdjuntos = DatabaseHelper.ObtenerDocumentos("DescargaHub_" + rec.Hermano);
                }

                TxtSinNombramientos.Visibility = Visibility.Collapsed;
                TablaNombramientos.Visibility = Visibility.Visible;

                var collectionView = CollectionViewSource.GetDefaultView(recomendaciones);
                collectionView.GroupDescriptions.Clear();
                collectionView.GroupDescriptions.Add(new PropertyGroupDescription("Congregacion"));

                TablaNombramientos.ItemsSource = collectionView;
            }
            else
            {
                TablaNombramientos.Visibility = Visibility.Collapsed;
                TxtSinNombramientos.Visibility = Visibility.Visible;
            }
        }

        private async void BtnCancelarRecomendacion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            if (boton == null || !(boton.Tag is RecomendacionHub rec)) return;

            if (MessageBox.Show($"¿Estás seguro de querer cancelar esta recomendación para {rec.Hermano}?", "Confirmar Cancelación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

                boton.IsEnabled = false;
                //SpinnerCargando.Visibility = Visibility.Visible;

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

                    MessageBox.Show($"La recomendación de {rec.Hermano} se ha cancelado en JW Hub.\nEl registro ha sido eliminado de la bandeja.", "Cancelado", MessageBoxButton.OK, MessageBoxImage.Information);

                    await CargarListaPersonasAsync();
                    CargarNombramientosAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error durante la automatización: {ex.Message}", "Fallo", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    boton.IsEnabled = true;
                    //if (!SincronizadorJWHub.EstaSincronizando)
                        //SpinnerCargando.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void BtnAprobarRecomendacion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            if (boton == null || !(boton.Tag is RecomendacionHub rec)) return;

            var ventana = new AprobarNombramientoWindow(rec);
            ventana.Owner = Window.GetWindow(this);

            if (ventana.ShowDialog() == true)
            {
                string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

                boton.IsEnabled = false;
                //SpinnerCargando.Visibility = Visibility.Visible;

                try
                {
                    await Task.Run(() => SincronizadorJWHub.AprobarRecomendacionYEnviarCorreo(folderPath, rec, ventana.FechaSeleccionada, ventana.ArchivosPdfGenerados));

                    var personaBD = _todasLasPersonas.FirstOrDefault(x =>
                        (!string.IsNullOrEmpty(x.Nombre) && x.Nombre.Contains(rec.Hermano, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(x.NombreComun) && x.NombreComun.Contains(rec.Hermano, StringComparison.OrdinalIgnoreCase)));

                    if (personaBD != null && !string.IsNullOrEmpty(rec.RecomendadoComo))
                    {
                        if (rec.RecomendadoComo.Contains("Anciano", StringComparison.OrdinalIgnoreCase)) personaBD.Nombramiento = "Anciano";
                        else if (rec.RecomendadoComo.Contains("Siervo", StringComparison.OrdinalIgnoreCase)) personaBD.Nombramiento = "Siervo ministerial";
                        DatabaseHelper.ActualizarPersona(personaBD);
                    }

                    DatabaseHelper.EliminarRecomendacion(rec.Hermano, rec.Congregacion);

                    var archivosViejos = DatabaseHelper.ObtenerDocumentos("DescargaHub_" + rec.Hermano);
                    foreach (var arc in archivosViejos)
                    {
                        DatabaseHelper.EliminarDocumento(arc.Id);
                        if (File.Exists(arc.RutaLocal)) File.Delete(arc.RutaLocal);
                    }

                    MessageBox.Show($"¡ÉXITO TOTAL!\nLa recomendación de {rec.Hermano} se ha aprobado y el correo se ha generado.\nEl registro ha sido eliminado de la bandeja de pendientes.", "Aprobado", MessageBoxButton.OK, MessageBoxImage.Information);

                    await CargarListaPersonasAsync();
                    CargarNombramientosAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error durante la automatización: {ex.Message}", "Fallo", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    boton.IsEnabled = true;
                    //if (!SincronizadorJWHub.EstaSincronizando)
                        //SpinnerCargando.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void BtnAbrirArchivoNombramiento_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button ?? sender as Wpf.Ui.Controls.Button;
            if (boton == null) return;

            var archivo = boton.Tag as ArchivoDocumento;

            if (File.Exists(archivo.RutaLocal))
            {
                Process.Start(new ProcessStartInfo(archivo.RutaLocal) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("El archivo no se encuentra en el disco. Quizás fue eliminado o movido manualmente de la carpeta Documentos.", "Archivo no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnSincronizarHub_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

            // Bloqueamos el botón y guardamos su texto original para recuperarlo después
            string textoOriginal = BtnSincronizarHub.Content?.ToString() ?? "Sintonizar JW Hub";
            BtnSincronizarHub.IsEnabled = false;

            try
            {
                var personasExtraidas = await Task.Run(() => SincronizadorJWHub.SincronizarGlobal(folderPath));

                MessageBox.Show($"¡Prueba en segundo plano completada!\nSe extrajeron {personasExtraidas?.Count ?? 0} personas de JW Hub.", "Sincronización JW Hub", MessageBoxButton.OK, MessageBoxImage.Information);

                if (personasExtraidas != null && personasExtraidas.Count > 0)
                {
                    var listaCongresBD = DatabaseHelper.ObtenerCongregaciones();

                    var personasParaBD = personasExtraidas.Select(pe =>
                    {
                        string congreHub = pe.Congregacion ?? "";
                        string codigoDetectado = "";
                        string nombreLimpiado = congreHub;

                        int start = congreHub.LastIndexOf('(');
                        int end = congreHub.LastIndexOf(')');
                        if (start >= 0 && end > start)
                        {
                            codigoDetectado = congreHub.Substring(start + 1, end - start - 1).Trim();
                            nombreLimpiado = congreHub.Substring(0, start).Trim();
                        }

                        var congreMatch = listaCongresBD.FirstOrDefault(c =>
                            (!string.IsNullOrEmpty(codigoDetectado) && c.Codigo == codigoDetectado) ||
                            (c.Nombre != null && c.Nombre.Equals(nombreLimpiado, StringComparison.OrdinalIgnoreCase)) ||
                            (c.Nombre != null && congreHub.Contains(c.Nombre)));

                        if (congreMatch != null)
                        {
                            codigoDetectado = congreMatch.Codigo;
                            nombreLimpiado = congreMatch.Nombre;
                        }

                        return new Persona
                        {
                            Nombre = pe.NombreCompleto,
                            NombreJWHub = pe.NombreCompleto,
                            NombreComun = pe.NombreComun,
                            Sexo = "",
                            Esperanza = pe.Esperanza,
                            FechaNacimiento = pe.FechaNacimiento,
                            FechaBautismo = pe.FechaBautismo,
                            FechaInicioPrec = pe.FechaInicioPrec,
                            FechaUltimaEscuela = pe.FechaUltimaEscuela,
                            Solicitudes = pe.Solicitudes,
                            Nombramiento = pe.Nombramiento,
                            Servicio = pe.Servicio,
                            Privilegios = pe.Privilegios,
                            CongregacionNombre = nombreLimpiado,
                            CongregacionCodigo = codigoDetectado,
                            Telefono = pe.Telefono,
                            Email = pe.Email,
                            EmailJW = pe.EmailJW,
                            Direccion = pe.Direccion,
                            Foto = pe.Foto
                        };
                    }).ToList();

                    var ventana = new SyncHubWindow(personasParaBD);
                    ventana.Owner = Window.GetWindow(this);

                    if (ventana.ShowDialog() == true)
                    {
                        await CargarListaPersonasAsync();
                    }
                }

                CargarNombramientosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error de automatización: {ex.Message}", "Fallo de Sincronización", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSincronizarHub.Content = textoOriginal;
                BtnSincronizarHub.IsEnabled = true;
            }
        }

        private async void BtnEditarPersona_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var personaSeleccionada = boton.DataContext as Persona;

            if (personaSeleccionada != null)
            {
                FormularioPersonaWindow ventana = new FormularioPersonaWindow(personaSeleccionada);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    if (!ventana.EsUnaFusion())
                    {
                        DatabaseHelper.ActualizarPersona(ventana.PersonaActualizada);
                    }

                    await CargarListaPersonasAsync();
                }
            }
        }

        private async void BtnEliminarPersona_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var personaSeleccionada = boton.DataContext as Persona;

            if (personaSeleccionada != null)
            {
                var resultado = MessageBox.Show($"¿Estás seguro de que deseas eliminar permanentemente a {personaSeleccionada.Nombre}?\nSe borrará también todo su historial de actividad.",
                                                "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (resultado == MessageBoxResult.Yes)
                {
                    DatabaseHelper.EliminarPersona(personaSeleccionada.Id);
                    await CargarListaPersonasAsync();
                }
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tc && tc.SelectedIndex == 1 && _todasLasPersonas != null)
            {
                var pendientes = _todasLasPersonas.Where(p => !string.IsNullOrWhiteSpace(p.Direccion) && p.Latitud == 0 && p.Longitud == 0).ToList();

                if (pendientes.Count > 0)
                {
                    string htmlCarga = $@"
                    <!DOCTYPE html>
                    <html><head>
                        <meta charset='UTF-8'>
                        <style>body{{background:#1E1E1E;color:#4BA3E3;display:flex;flex-direction:column;justify-content:center;align-items:center;height:100vh;font-family:'Segoe UI',sans-serif;text-align:center;}}</style>
                    </head><body>
                        <h2>Geolocalizando direcciones en el mapa...</h2>
                        <p>Convirtiendo textos a coordenadas ({pendientes.Count} pendientes).</p>
                        <p style='color:#AAAAAA; font-size:14px; margin-top:20px;'>(Este proceso protege tu IP evitando saturar los servidores de OpenStreetMap).</p>
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

                DibujarMapa();
            }
        }

        private void DibujarMapa()
        {
            var sb = new System.Text.StringBuilder();

            var personasConCoords = _todasLasPersonas.Where(p => p.Latitud != 0 && p.Latitud != 999 && p.Longitud != 0 && p.Longitud != 999).ToList();

            foreach (var p in personasConCoords)
            {
                string nombreLimpio = p.Nombre != null ? p.Nombre.Replace("'", "\\'") : "Desconocido";
                string dirLimpia = (p.Direccion ?? "").Replace("'", "\\'");

                sb.AppendLine($"L.marker([{p.Latitud.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {p.Longitud.ToString(System.Globalization.CultureInfo.InvariantCulture)}]).addTo(map).bindPopup('<b>{nombreLimpio}</b><br>{dirLimpia}');");
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
                    
                    {sb.ToString()}
                    
                    var markers = [];
                    map.eachLayer(function (layer) {{ if (layer instanceof L.Marker) markers.push(layer.getLatLng()); }});
                    if (markers.length > 0) map.fitBounds(L.latLngBounds(markers), {{padding: [30, 30]}});
                </script>
            </body></html>";

            MapaWebBrowser.NavigateToString(html);
        }

        private void BtnFiltroCongre_Click(object sender, RoutedEventArgs e) { PopFiltroCongre.IsOpen = true; }
        private void BtnFiltroServicio_Click(object sender, RoutedEventArgs e) { PopFiltroServicio.IsOpen = true; }
        private void BtnFiltroSols_Click(object sender, RoutedEventArgs e) { PopFiltroSols.IsOpen = true; }
        private void BtnFiltroPrivs_Click(object sender, RoutedEventArgs e) { PopFiltroPrivs.IsOpen = true; }

        private void LstFiltro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            BtnFiltroCongre.Content = LstCongre.SelectedItems.Count > 0 ? $"{LstCongre.SelectedItems.Count} selec." : "Todas ▼";
            BtnFiltroServicio.Content = LstServicio.SelectedItems.Count > 0 ? $"{LstServicio.SelectedItems.Count} selec." : "Todos ▼";
            BtnFiltroSols.Content = LstSols.SelectedItems.Count > 0 ? $"{LstSols.SelectedItems.Count} selec." : "Todas ▼";
            BtnFiltroPrivs.Content = LstPrivs.SelectedItems.Count > 0 ? $"{LstPrivs.SelectedItems.Count} selec." : "Todos ▼";

            _timerBusqueda.Stop();
            _timerBusqueda.Start();
        }

        private void FiltroCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _timerBusqueda.Stop();
            _timerBusqueda.Start();
        }

        private void BtnLimpiarFiltros_Click(object sender, RoutedEventArgs e)
        {
            TxtBuscar.Text = "";
            CmbFiltroNombramiento.SelectedIndex = 0;
            CmbFiltroSexo.SelectedIndex = 0;
            CmbFiltroEsperanza.SelectedIndex = 0;

            LstCongre.SelectedItems.Clear();
            LstServicio.SelectedItems.Clear();
            LstSols.SelectedItems.Clear();
            LstPrivs.SelectedItems.Clear();

            AplicarFiltroYPaginar();
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            _timerBusqueda.Stop();
            _timerBusqueda.Start();
        }

        private void TimerBusqueda_Tick(object sender, EventArgs e)
        {
            _timerBusqueda.Stop();
            AplicarFiltroYPaginar();
        }

        private void TablaPersonas_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;

            string propertyName = e.Column.SortMemberPath;
            if (string.IsNullOrEmpty(propertyName) && e.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding)
            {
                propertyName = binding.Path.Path;
            }

            if (string.IsNullOrEmpty(propertyName)) return;

            if (_columnaOrdenActual == propertyName)
            {
                _direccionOrdenActual = _direccionOrdenActual == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }
            else
            {
                _columnaOrdenActual = propertyName;
                _direccionOrdenActual = ListSortDirection.Ascending;
            }

            e.Column.SortDirection = _direccionOrdenActual;

            foreach (var col in TablaPersonas.Columns)
            {
                if (col != e.Column) col.SortDirection = null;
            }

            AplicarFiltroYPaginar();
        }

        private object ObtenerValorPropiedad(Persona p, string propName)
        {
            return p.GetType().GetProperty(propName)?.GetValue(p, null) ?? "";
        }

        private void AplicarFiltroYPaginar()
        {
            string busqueda = TxtBuscar.Text.Trim().ToLower();
            string nombramiento = (CmbFiltroNombramiento.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            string sexo = (CmbFiltroSexo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            string esperanza = (CmbFiltroEsperanza.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

            var congreSels = LstCongre.SelectedItems.Cast<string>().ToList();
            var servSels = LstServicio.SelectedItems.Cast<string>().ToList();
            var solSels = LstSols.SelectedItems.Cast<string>().ToList();
            var privSels = LstPrivs.SelectedItems.Cast<string>().ToList();

            _personasFiltradas = _todasLasPersonas.Where(p =>
                ((p.Nombre != null && p.Nombre.ToLower().Contains(busqueda)) || (p.NombreComun != null && p.NombreComun.ToLower().Contains(busqueda))) &&
                (string.IsNullOrEmpty(nombramiento) || p.Nombramiento == nombramiento) &&
                (string.IsNullOrEmpty(sexo) || p.Sexo == sexo) &&
                (string.IsNullOrEmpty(esperanza) || p.Esperanza == esperanza) &&
                (congreSels.Count == 0 || congreSels.Contains(p.CongregacionNombre)) &&
                (servSels.Count == 0 || servSels.Contains(p.Servicio)) &&
                (solSels.Count == 0 || solSels.Any(s => p.Solicitudes != null && p.Solicitudes.Contains(s))) &&
                (privSels.Count == 0 || privSels.Any(pr => p.Privilegios != null && p.Privilegios.Contains(pr)))
            ).ToList();

            if (!string.IsNullOrEmpty(_columnaOrdenActual))
            {
                if (_direccionOrdenActual == ListSortDirection.Ascending)
                    _personasFiltradas = _personasFiltradas.OrderBy(p => ObtenerValorPropiedad(p, _columnaOrdenActual)).ToList();
                else
                    _personasFiltradas = _personasFiltradas.OrderByDescending(p => ObtenerValorPropiedad(p, _columnaOrdenActual)).ToList();
            }

            _totalPaginas = (int)Math.Ceiling(_personasFiltradas.Count / (double)_tamanoPagina);
            if (_totalPaginas == 0) _totalPaginas = 1;

            if (_paginaActual > _totalPaginas) _paginaActual = 1;

            RenderizarPagina();
        }

        private void RenderizarPagina()
        {
            var personasPagina = _personasFiltradas.Skip((_paginaActual - 1) * _tamanoPagina).Take(_tamanoPagina).ToList();

            TablaPersonas.ItemsSource = personasPagina;
            TxtPaginaInfo.Text = $"{_personasFiltradas.Count} resultados | Página {_paginaActual} de {_totalPaginas}";

            BtnAnterior.IsEnabled = _paginaActual > 1;
            BtnSiguiente.IsEnabled = _paginaActual < _totalPaginas;
        }

        private void BtnAnterior_Click(object sender, RoutedEventArgs e)
        {
            if (_paginaActual > 1) { _paginaActual--; RenderizarPagina(); }
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            if (_paginaActual < _totalPaginas) { _paginaActual++; RenderizarPagina(); }
        }

        public class NominatimResult
        {
            public string lat { get; set; }
            public string lon { get; set; }
        }
    }

    public class Base64ImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string base64 = value as string;
            if (string.IsNullOrEmpty(base64)) return null;

            try
            {
                byte[] imageBytes = System.Convert.FromBase64String(base64);
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}