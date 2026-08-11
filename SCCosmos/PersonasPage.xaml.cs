using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class PersonasPage : Page
    {
        // Variables para la paginación y búsqueda
        private List<Persona> _todasLasPersonas = new List<Persona>();
        private List<Persona> _personasFiltradas = new List<Persona>();
        private int _paginaActual = 1;
        private int _tamanoPagina = 20;
        private int _totalPaginas = 1;

        public PersonasPage()
        {
            InitializeComponent();
            ComprobarPerfilFirefox();

            // Usamos Loaded para poder llamar al método asíncrono y ver el spinner al arrancar
            this.Loaded += async (s, e) => await CargarListaPersonasAsync();
        }

        private void ComprobarPerfilFirefox()
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
            if (Directory.Exists(folderPath))
            {
                if (BtnSincronizarHub != null) BtnSincronizarHub.Visibility = Visibility.Visible;
            }
        }

        // ==========================================
        // CARGA ASÍNCRONA DE DATOS Y FILTRADO
        // ==========================================
        private async Task CargarListaPersonasAsync()
        {
            SpinnerCargando.Visibility = Visibility.Visible;
            TablaPersonas.ItemsSource = null;

            // Leemos la base de datos en un hilo secundario para no congelar la pantalla
            _todasLasPersonas = await Task.Run(() => DatabaseHelper.ObtenerPersonas());

            // =========================================================
            // ESTANDARIZAR FORMATO DE FECHAS (dd/MM/yyyy)
            // =========================================================
            foreach (var p in _todasLasPersonas)
            {
                if (!string.IsNullOrWhiteSpace(p.FechaNacimiento) && DateTime.TryParse(p.FechaNacimiento, out DateTime fn))
                {
                    p.FechaNacimiento = fn.ToString("dd/MM/yyyy");
                }

                if (!string.IsNullOrWhiteSpace(p.FechaBautismo) && DateTime.TryParse(p.FechaBautismo, out DateTime fb))
                {
                    p.FechaBautismo = fb.ToString("dd/MM/yyyy");
                }
            }
            // =========================================================

            AplicarFiltroYPaginar();

            SpinnerCargando.Visibility = Visibility.Collapsed;
        }

        private void AplicarFiltroYPaginar()
        {
            string busqueda = TxtBuscar.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(busqueda))
            {
                _personasFiltradas = _todasLasPersonas.ToList();
            }
            else
            {
                // El buscador mira en toda la lista base, no importa en qué página estés
                _personasFiltradas = _todasLasPersonas
                    .Where(p => p.Nombre != null && p.Nombre.ToLower().Contains(busqueda))
                    .ToList();
            }

            // Recalculamos las páginas y devolvemos al usuario a la página 1 tras buscar
            _totalPaginas = (int)Math.Ceiling(_personasFiltradas.Count / (double)_tamanoPagina);
            if (_totalPaginas == 0) _totalPaginas = 1;
            _paginaActual = 1;

            RenderizarPagina();
        }

        private void RenderizarPagina()
        {
            // Cortamos el "trozo" de 25 personas que corresponde a la página actual
            var personasPagina = _personasFiltradas
                .Skip((_paginaActual - 1) * _tamanoPagina)
                .Take(_tamanoPagina)
                .ToList();

            TablaPersonas.ItemsSource = personasPagina;

            TxtPaginaInfo.Text = $"Página {_paginaActual} de {_totalPaginas}";

            BtnAnterior.IsEnabled = _paginaActual > 1;
            BtnSiguiente.IsEnabled = _paginaActual < _totalPaginas;
        }

        // ==========================================
        // EVENTOS DE LA INTERFAZ
        // ==========================================
        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Filtramos en tiempo real sin llamar a la base de datos otra vez
            AplicarFiltroYPaginar();
        }

        private void BtnAnterior_Click(object sender, RoutedEventArgs e)
        {
            if (_paginaActual > 1)
            {
                _paginaActual--;
                RenderizarPagina();
            }
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            if (_paginaActual < _totalPaginas)
            {
                _paginaActual++;
                RenderizarPagina();
            }
        }

        private async void BtnSincronizarHub_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
            MessageBox.Show("Se va a abrir Firefox.\nRecorreremos Precursores, Ancianos y Siervos automáticamente. Por favor, no toques el navegador.", "Sincronizando...", MessageBoxButton.OK, MessageBoxImage.Information);
            BtnSincronizarHub.IsEnabled = false;

            try
            {
                var personasObtenidas = await Task.Run(() => SincronizadorJWHub.Sincronizar(folderPath));

                if (personasObtenidas != null)
                {
                    if (personasObtenidas.Count == 0)
                    {
                        MessageBox.Show("No se detectó información. ¿Quizás caducó tu sesión o la congregación no tiene ningún siervo/anciano/precursor?", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var personasDb = await Task.Run(() => DatabaseHelper.ObtenerPersonas());
                    var listaNuevos = new List<PersonaExtraida>();
                    int actualizadosAuto = 0;

                    // Filtramos los de nuestra base de datos que ya tenían un nombre JW Hub asociado
                    var dbVinculadas = personasDb.Where(p => !string.IsNullOrWhiteSpace(p.NombreJWHub)).ToList();

                    // 1. REVISAR A LOS EXISTENTES (Actualizarlos o quitarles los privilegios perdidos)
                    foreach (var dbPerson in dbVinculadas)
                    {
                        var extraido = personasObtenidas.FirstOrDefault(p => p.NombreCompleto == dbPerson.NombreJWHub);

                        if (extraido != null)
                        {
                            // Sigue teniendo privilegios en JW Hub: Lo actualizamos con sus datos nuevos
                            DatabaseHelper.ActualizarDatosPersonaImportada(dbPerson.Id, extraido);
                            actualizadosAuto++;
                        }
                        else
                        {
                            // Ya NO tiene privilegios (No apareció en ninguna pestaña de JW Hub)
                            // Le borramos Servicio y Nombramiento (pero mantenemos sus datos de contacto y su nombre JW Hub intactos)
                            if (!string.IsNullOrEmpty(dbPerson.Nombramiento) || !string.IsNullOrEmpty(dbPerson.Servicio) || !string.IsNullOrEmpty(dbPerson.Solicitudes))
                            {
                                var dummy = new PersonaExtraida
                                {
                                    NombreCompleto = dbPerson.NombreJWHub,
                                    Congregacion = dbPerson.CongregacionCodigo,
                                    Servicio = "",
                                    Nombramiento = "",
                                    Solicitudes = "",
                                    Privilegios = ""
                                };
                                DatabaseHelper.ActualizarDatosPersonaImportada(dbPerson.Id, dummy);
                                actualizadosAuto++;
                            }
                        }
                    }

                    // 2. DETECTAR A LOS NUEVOS (Los que ha extraído pero no están en la BBDD vinculados)
                    foreach (var prec in personasObtenidas)
                    {
                        if (!dbVinculadas.Any(p => p.NombreJWHub == prec.NombreCompleto))
                        {
                            listaNuevos.Add(prec);
                        }
                    }

                    // Lanzar ventana si hay nuevos, si no, notificar éxito
                    if (listaNuevos.Count > 0)
                    {
                        var ventana = new EmparejarPersonasWindow(listaNuevos);
                        ventana.Owner = Window.GetWindow(this);
                        if (ventana.ShowDialog() == true) await CargarListaPersonasAsync();
                    }
                    else
                    {
                        MessageBox.Show($"Sincronización completada en segundo plano.\n\nSe actualizaron {actualizadosAuto} personas que ya tenías vinculadas.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                        await CargarListaPersonasAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la lectura: {ex.Message}", "Error de sincronización", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
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
                    DatabaseHelper.ActualizarPersona(ventana.PersonaActualizada);
                    await CargarListaPersonasAsync(); // Recargamos para refrescar la vista
                }
            }
        }

        private void BtnVerActividad_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var personaSeleccionada = boton.DataContext as Persona;

            if (personaSeleccionada != null)
            {
                ActividadPersona ventanaActividad = new ActividadPersona(personaSeleccionada);
                ventanaActividad.Owner = Window.GetWindow(this);
                ventanaActividad.ShowDialog();
            }
        }

        private async void BtnEliminarPersona_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var personaSeleccionada = boton.DataContext as Persona;

            if (personaSeleccionada != null)
            {
                var resultado = MessageBox.Show($"¿Estás seguro de que deseas eliminar permanentemente a {personaSeleccionada.Nombre}? Se borrará también todo su historial de actividad.",
                                                "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (resultado == MessageBoxResult.Yes)
                {
                    DatabaseHelper.EliminarPersona(personaSeleccionada.Id);
                    await CargarListaPersonasAsync(); // Volvemos a leer BBDD y repaginar
                }
            }
        }
    }
}