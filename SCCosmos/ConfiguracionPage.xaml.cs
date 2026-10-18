using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;

namespace SCCosmos
{
    // Clase para guardar las preferencias locales en el AppData (no en la BBDD)
    public class ConfiguracionLocalSettings
    {
        public bool AutoDescargarMartes { get; set; } = false;
        public bool AutoSincronizarLunes { get; set; } = false;
        public string RutaDescargas { get; set; } = "";
    }

    public partial class ConfiguracionPage : Page
    {
        private string _rutaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos");
        private string _rutaArchivoLocalSettings;

        public ConfiguracionPage()
        {
            InitializeComponent();

            _rutaArchivoLocalSettings = Path.Combine(_rutaAppData, "local_settings.json");

            HookAutoguardado();

            // Versión y actualizaciones.
            TxtVersionInstalada.Text =
                "Versión instalada: "
                + ActualizacionPc.VersionInstalada;

            ToggleActualizarAuto.IsChecked =
                ActualizacionPc.AutoActiva;

            ToggleActualizarAuto.Checked += (s, e) =>
                ActualizacionPc.AutoActiva = true;

            ToggleActualizarAuto.Unchecked += (s, e) =>
                ActualizacionPc.AutoActiva = false;

            this.Loaded += ConfiguracionPage_Loaded;
            this.Unloaded += ConfiguracionPage_Unloaded;

            // La esposa del SC no ve el JW Hub ni los datos del
            // superintendente y del circuito: solo su propio nombre.
            if (!PerfilAcceso.AccesoCompleto)
            {
                CardJwHub.Visibility = Visibility.Collapsed;
                CardArchivoCircuito.Visibility = Visibility.Collapsed;
                CardSincronizacion.Visibility = Visibility.Collapsed;
                ToggleSincronizacion.Visibility = Visibility.Collapsed;

                LblNumCircuito.Visibility = Visibility.Collapsed;
                TxtNumCircuito.Visibility = Visibility.Collapsed;
                LblNombreSC.Visibility = Visibility.Collapsed;
                TxtNombre.Visibility = Visibility.Collapsed;
                LblApellidoSC.Visibility = Visibility.Collapsed;
                TxtApellido.Visibility = Visibility.Collapsed;
            }
        }

        private async void ConfiguracionPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Dejar que la página se pinte antes de cargar los datos.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            ComprobarEstadoVinculacion();
            CargarConfiguracion();
            CargarPerfilUsuario();
            CargarSincronizacion();
        }

        // =========================================================================
        // VERSIÓN Y ACTUALIZACIONES
        // =========================================================================

        private async void BtnBuscarActualizacion_Click(
            object sender,
            RoutedEventArgs e)
        {
            BtnBuscarActualizacion.IsEnabled = false;
            TxtEstadoActualizacion.Text = "Buscando…";

            try
            {
                var resultado =
                    await ActualizacionPc.ComprobarAsync();

                if (!resultado.Correcto)
                {
                    TxtEstadoActualizacion.Text =
                        "No se pudo comprobar: "
                        + resultado.Error;

                    return;
                }

                if (resultado.Version == null)
                {
                    TxtEstadoActualizacion.Text =
                        "Todavía no hay ninguna versión publicada.";

                    return;
                }

                if (!ActualizacionPc.HayNueva(resultado))
                {
                    TxtEstadoActualizacion.Text =
                        "Estás al día (v"
                        + ActualizacionPc.VersionInstalada
                        + ").";

                    return;
                }

                var version =
                    resultado.Version;

                var respuesta = MessageBox.Show(
                    "Hay una versión nueva de SCCosmos (v"
                    + version.Numero + ").\n\n"
                    + RecortarNotas(version.Notas)
                    + "\n¿Actualizar ahora? La aplicación se cerrará "
                    + "y se volverá a abrir ya actualizada.",
                    "Actualización disponible",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (respuesta != MessageBoxResult.Yes)
                {
                    TxtEstadoActualizacion.Text =
                        "Actualización aplazada (v"
                        + version.Numero + ").";

                    return;
                }

                TxtEstadoActualizacion.Text =
                    "Descargando la versión nueva…";

                bool preparada =
                    await ActualizacionPc.PrepararAsync(
                        version,
                        progreso =>
                            Dispatcher.Invoke(() =>
                                TxtEstadoActualizacion.Text =
                                    "Descargando la versión nueva… "
                                    + progreso.ToString("P0")));

                if (!preparada)
                {
                    TxtEstadoActualizacion.Text =
                        "No se pudo preparar la actualización.";

                    return;
                }

                TxtEstadoActualizacion.Text =
                    "Aplicando la actualización…";

                if (ActualizacionPc.AplicarAhora())
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    TxtEstadoActualizacion.Text =
                        "No se pudo aplicar la actualización.";
                }
            }
            catch (Exception ex)
            {
                TxtEstadoActualizacion.Text =
                    "No se pudo comprobar: " + ex.Message;
            }
            finally
            {
                BtnBuscarActualizacion.IsEnabled = true;
            }
        }


        private static string RecortarNotas(
            string notas)
        {
            string texto =
                (notas ?? "").Trim();

            if (texto.Length > 400)
            {
                texto =
                    texto.Substring(0, 400) + "…";
            }

            return texto.Length == 0
                ? ""
                : texto + "\n\n";
        }

        // =========================================================================
        // PERFIL DE USUARIO
        // =========================================================================

        private void CargarPerfilUsuario()
        {
            var datos = PerfilAcceso.Cargar();

            CmbPerfilUsuario.SelectedIndex =
                string.Equals(
                    datos.Rol,
                    PerfilAcceso.PerfilEsposa,
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;

            TxtCodigoPerfil.Password =
                datos.CodigoOk
                    ? PerfilAcceso.CodigoCompleto
                    : "";

            ActualizarPanelCodigoPerfil();
            ActualizarEstadoPerfil();
        }

        /// <summary>
        /// El código de acceso es solo para el superintendente: el campo se
        /// oculta mientras el perfil seleccionado no sea «Hombre».
        /// </summary>
        private void ActualizarPanelCodigoPerfil()
        {
            // Al cargar el XAML, el ComboBox puede disparar SelectionChanged
            // antes de que existan los demás controles de la página.
            if (PanelCodigoPerfil == null
                || TxtCodigoPerfil == null)
            {
                return;
            }

            string rol =
                (CmbPerfilUsuario.SelectedItem as ComboBoxItem)
                    ?.Content?.ToString()
                ?? PerfilAcceso.PerfilHombre;

            bool esHombre =
                string.Equals(
                    rol,
                    PerfilAcceso.PerfilHombre,
                    StringComparison.OrdinalIgnoreCase);

            PanelCodigoPerfil.Visibility =
                esHombre
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (!esHombre)
            {
                TxtCodigoPerfil.Password = "";
            }
        }

        private void CmbPerfilUsuario_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            ActualizarPanelCodigoPerfil();
        }

        private void ActualizarEstadoPerfil()
        {
            if (TxtEstadoPerfil == null)
            {
                return;
            }

            bool completo =
                PerfilAcceso.AccesoCompleto;

            TxtEstadoPerfil.Text =
                completo
                    ? "Acceso completo a todas las funciones."
                    : "Acceso limitado: Inicio, Calendario, Personas y Configuración.";

            TxtEstadoPerfil.Foreground =
                new System.Windows.Media.SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(
                        completo ? "#4CAF50" : "#EAC18B"));
        }

        private void BtnGuardarPerfil_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                string rol =
                    (CmbPerfilUsuario.SelectedItem as ComboBoxItem)
                        ?.Content?.ToString()
                    ?? PerfilAcceso.PerfilHombre;

                PerfilAcceso.EstablecerRol(rol);

                if (PerfilAcceso.EsHombre)
                {
                    PerfilAcceso.ValidarCodigo(
                        TxtCodigoPerfil.Password);
                }

                ActualizarEstadoPerfil();

                (Window.GetWindow(this) as MainWindow)
                    ?.AplicarPerfilAcceso();

                bool codigoEspecial =
                    string.Equals(
                        (TxtCodigoPerfil.Password ?? "").Trim(),
                        PerfilAcceso.CodigoDescargas,
                        StringComparison.OrdinalIgnoreCase);

                MessageBox.Show(
                    PerfilAcceso.AccesoCompleto
                        ? codigoEspecial
                            ? "Guardado. Acceso completo a todas las funciones y página de Descargas activada para siempre."
                            : "Guardado. Acceso completo a todas las funciones."
                        : "Guardado. Acceso limitado a Inicio, Calendario, Personas y Configuración.",
                    "Perfil de usuario",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo guardar el perfil: " + ex.Message,
                    "Perfil de usuario",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CargarConfiguracion()
        {
            _autoguardando = true;

            // 1. CARGAR DATOS GLOBALES (BBDD)
            TxtNombre.Text = DatabaseHelper.ObtenerConfiguracion("NombreSC", "");
            TxtApellido.Text = DatabaseHelper.ObtenerConfiguracion("ApellidoSC", "");
            TxtNombreEsposa.Text = DatabaseHelper.ObtenerConfiguracion("NombreEsposaSC", "");
            TxtApellidoEsposa.Text = DatabaseHelper.ObtenerConfiguracion("ApellidoEsposaSC", "");
            TxtNumCircuito.Text = DatabaseHelper.ObtenerConfiguracion("NumeroCircuito", "");
            TxtRutaArchivoCircuito.Text = DatabaseHelper.ObtenerConfiguracion("RutaArchivoCircuito", "");

            // 2. CARGAR DATOS LOCALES (Este PC)
            TxtRutaBaseDatos.Text = DatabaseHelper.RutaBaseDatos;

            if (File.Exists(_rutaArchivoLocalSettings))
            {
                try
                {
                    string json = File.ReadAllText(_rutaArchivoLocalSettings);
                    var localSettings = JsonSerializer.Deserialize<ConfiguracionLocalSettings>(json);

                    ToggleDescargas.IsChecked = localSettings.AutoDescargarMartes;
                    ToggleSincronizacion.IsChecked = localSettings.AutoSincronizarLunes;
                    TxtRutaDescargas.Text = localSettings.RutaDescargas;
                }
                catch { }
            }

            if (string.IsNullOrEmpty(TxtRutaDescargas.Text))
            {
                TxtRutaDescargas.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DescargasStream");
            }

            _autoguardando = false;
        }

        /// <summary>
        /// Guarda toda la configuración (sin botones: se llama sola).
        /// </summary>
        private void GuardarTodo()
        {
            try
            {
                if (_autoguardando)
                {
                    return;
                }

                // 1. GUARDAR DATOS GLOBALES
                DatabaseHelper.GuardarConfiguracion("NombreSC", TxtNombre.Text.Trim());
                DatabaseHelper.GuardarConfiguracion("ApellidoSC", TxtApellido.Text.Trim());
                DatabaseHelper.GuardarConfiguracion("NombreEsposaSC", TxtNombreEsposa.Text.Trim());
                DatabaseHelper.GuardarConfiguracion("ApellidoEsposaSC", TxtApellidoEsposa.Text.Trim());
                DatabaseHelper.GuardarConfiguracion("NumeroCircuito", TxtNumCircuito.Text.Trim());
                DatabaseHelper.GuardarConfiguracion("RutaArchivoCircuito", TxtRutaArchivoCircuito.Text.Trim());

                // 2. GUARDAR DATOS LOCALES
                if (!Directory.Exists(_rutaAppData)) Directory.CreateDirectory(_rutaAppData);

                var localSettings = new ConfiguracionLocalSettings
                {
                    AutoDescargarMartes = ToggleDescargas.IsChecked ?? false,
                    AutoSincronizarLunes = ToggleSincronizacion.IsChecked ?? false,
                    RutaDescargas = TxtRutaDescargas.Text.Trim()
                };
                File.WriteAllText(_rutaArchivoLocalSettings, JsonSerializer.Serialize(localSettings));
            }
            catch
            {
            }
        }


        // =========================================================================
        // AUTOGUARDADO (SIN BOTONES)
        // =========================================================================

        private bool _autoguardando;

        private void Autoguardar()
        {
            try
            {
                GuardarTodo();
            }
            catch
            {
            }
        }


        private void HookAutoguardado()
        {
            foreach (System.Windows.Controls.TextBox campo in
                     new System.Windows.Controls.TextBox[]
                     {
                         TxtNombre,
                         TxtApellido,
                         TxtNombreEsposa,
                         TxtApellidoEsposa,
                         TxtNumCircuito,
                         TxtRutaArchivoCircuito,
                         TxtRutaDescargas
                     })
            {
                campo.LostFocus += (s, e) => Autoguardar();
            }

            ToggleDescargas.Checked += (s, e) => Autoguardar();
            ToggleDescargas.Unchecked += (s, e) => Autoguardar();
            ToggleSincronizacion.Checked += (s, e) => Autoguardar();
            ToggleSincronizacion.Unchecked += (s, e) => Autoguardar();

            TxtNombreSincro.LostFocus += (s, e) => GuardarNombreSincro();
            ToggleSincroActivo.Checked += (s, e) => AplicarSincroAutomatico();
            ToggleSincroActivo.Unchecked += (s, e) => AplicarSincroAutomatico();
        }

        // =========================================================================
        // SINCRONIZACIÓN ENTRE DISPOSITIVOS
        // =========================================================================

        private System.Windows.Threading.DispatcherTimer? _timerSincro;

        private void CargarSincronizacion()
        {
            try
            {
                var config = SincronizacionRed.Config;

                if (config is null)
                {
                    return;
                }

                _autoguardando = true;

                ToggleSincroActivo.IsChecked = config.Activo;
                TxtNombreSincro.Text = config.NombreDispositivo;
                TxtClaveSincro.Text = config.Clave;

                _autoguardando = false;

                if (_timerSincro is null)
                {
                    _timerSincro = new System.Windows.Threading.DispatcherTimer();
                    _timerSincro.Interval = TimeSpan.FromSeconds(2);
                    _timerSincro.Tick += (s, e) => RefrescarEstadoSincro();
                }

                _timerSincro.Start();

                RefrescarEstadoSincro();
            }
            catch
            {
            }
        }

        private void ConfiguracionPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _timerSincro?.Stop();
            }
            catch
            {
            }
        }

        private void RefrescarEstadoSincro()
        {
            try
            {
                var servicio = SincronizacionRed.Servicio;

                if (servicio is null)
                {
                    LblEstadoSincro.Text = "Sincronización no iniciada.";
                    ListaDispositivosSincro.ItemsSource = null;
                    return;
                }

                var partes = new List<string>();

                if (!string.IsNullOrWhiteSpace(servicio.UltimoEstado))
                {
                    partes.Add(servicio.UltimoEstado);
                }

                if (servicio.UltimaActividad is not null)
                {
                    partes.Add("Última actividad: " + servicio.UltimaActividad.Value.ToString("HH:mm:ss"));
                }

                partes.Add($"Enviados: {servicio.CambiosEnviados} · Recibidos: {servicio.CambiosRecibidos}");

                LblEstadoSincro.Text = string.Join("  ·  ", partes);

                ListaDispositivosSincro.ItemsSource =
                    SincronizacionRed.ObtenerDispositivosVista();
            }
            catch
            {
            }
        }

        private void BtnCopiarClaveSincro_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtClaveSincro.Text ?? "");
                LblEstadoSincro.Text = "Clave copiada al portapapeles.";
            }
            catch
            {
            }
        }

        /// <summary>
        /// Aplica los ajustes de sincronización (sin botón: se llama sola
        /// al cambiar el interruptor o el nombre).
        /// </summary>
        private void AplicarSincroAutomatico()
        {
            try
            {
                if (_autoguardando)
                {
                    return;
                }

                var config = SincronizacionRed.Config;

                if (config is null)
                {
                    return;
                }

                bool activoAntes = config.Activo;

                config.Activo = ToggleSincroActivo.IsChecked == true;
                config.NombreDispositivo = TxtNombreSincro.Text?.Trim() ?? "";

                // Primero se guarda y después se reinicia, para que el
                // motor arranque ya con los valores nuevos.
                SincronizacionRed.Guardar();
                SincronizacionRed.Reiniciar();

                if (activoAntes != config.Activo)
                {
                    DatabaseHelper.AnotarActividad(
                        "Configuración",
                        config.Activo
                            ? "Activó la sincronización entre dispositivos"
                            : "Desactivó la sincronización entre dispositivos");
                }

                RefrescarEstadoSincro();
            }
            catch
            {
            }
        }


        /// <summary>
        /// Guarda solo el nombre del dispositivo de sincronización.
        /// </summary>
        private void GuardarNombreSincro()
        {
            try
            {
                if (_autoguardando)
                {
                    return;
                }

                var config = SincronizacionRed.Config;

                if (config is null)
                {
                    return;
                }

                string nombre = TxtNombreSincro.Text?.Trim() ?? "";

                if (!string.Equals(
                        config.NombreDispositivo,
                        nombre,
                        StringComparison.Ordinal))
                {
                    config.NombreDispositivo = nombre;
                    SincronizacionRed.Guardar();
                }
            }
            catch
            {
            }
        }


        /// <summary>
        /// Desmarca o marca un dispositivo para no sincronizar con él.
        /// </summary>
        private void ChkDispositivo_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_autoguardando)
                {
                    return;
                }

                if (sender is CheckBox chk
                    && chk.Tag is string id
                    && id.Length > 0)
                {
                    SincronizacionRed.EstablecerHabilitado(
                        id,
                        (chk.DataContext as DispositivoSyncPc)?.Nombre ?? "",
                        chk.IsChecked == true);
                }
            }
            catch
            {
            }
        }

        // =========================================================================
        // MÉTODOS DE EXAMINAR CARPETAS
        // =========================================================================

        private void BtnRutaArchivoCircuito_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta raíz del Archivo del Circuito",
                InitialDirectory = string.IsNullOrEmpty(TxtRutaArchivoCircuito.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : TxtRutaArchivoCircuito.Text
            };
            if (dialog.ShowDialog() == true)
            {
                TxtRutaArchivoCircuito.Text = dialog.FolderName;
                GuardarTodo();
            }
        }

        private void BtnRutaDescargas_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta para los discursos descargados",
                InitialDirectory = string.IsNullOrEmpty(TxtRutaDescargas.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : TxtRutaDescargas.Text
            };
            if (dialog.ShowDialog() == true)
            {
                TxtRutaDescargas.Text = dialog.FolderName;
                GuardarTodo();
            }
        }

        private void BtnCambiarRuta_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta general de archivos de SCCosmos",
                InitialDirectory = DatabaseHelper.RutaDirectorioDatos
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // La base de datos ya no se mueve: vive dentro de la
                    // aplicación. Esta carpeta se usa para el Archivo del
                    // circuito y demás archivos.
                    DatabaseHelper.RutaDirectorioDatos = dialog.FolderName;
                    TxtRutaBaseDatos.Text = DatabaseHelper.RutaBaseDatos;

                    DatabaseHelper.AnotarActividad(
                        "Configuración",
                        "Cambió la carpeta de archivos a " + dialog.FolderName);

                    MessageBox.Show("Carpeta actualizada.\n\nSe usará para el Archivo del circuito y los archivos relacionados.", "Carpeta", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Hubo un error al intentar cambiar la carpeta:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // =========================================================================
        // MÉTODOS DE VINCULACIÓN JW HUB
        // =========================================================================

        private void ComprobarEstadoVinculacion()
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

            if (Directory.Exists(folderPath))
            {
                TxtTituloJW.Text = "Cuenta de JW (Vinculada)";
                TxtTituloJW.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                IconoEstado.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));

                TxtDescripcion.Text = "Tu perfil exclusivo de Firefox ya está creado y se ha guardado tu sesión.";
                BtnVincularCorreo.Content = "Actualizar sesión";
            }
            else
            {
                TxtTituloJW.Text = "Cuenta de JW (No vinculada)";
                TxtTituloJW.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#809C80"));
                IconoEstado.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#809C80"));
                BtnVincularCorreo.Content = "Vincular";
            }
        }

        private void BtnVincularCorreo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                var service = FirefoxDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                var options = new FirefoxOptions();
                options.AddArgument("-profile");
                options.AddArgument(folderPath);
                options.AddArgument("-no-remote");

                MessageBox.Show(
                    "Se va a abrir una ventana de Firefox limpia.\n\n" +
                    "PASOS A SEGUIR:\n" +
                    "1. Inicia sesión en tu correo jwpub.org o tu cuenta de jw.org.\n" +
                    "2. Marca la casilla 'Mantener la sesión iniciada'.\n" +
                    "3. Cuando veas que la sesión se ha iniciado, cierra Firefox manualmente.\n\n" +
                    "Esto solo lo tendrás que hacer una vez.",
                    "Configuración de perfil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                IWebDriver driver = new FirefoxDriver(service, options);
                driver.Navigate().GoToUrl("https://hub.jw.org/");

                ComprobarEstadoVinculacion();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al configurar el perfil de Firefox: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}