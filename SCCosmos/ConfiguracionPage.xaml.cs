using System;
using System.IO;
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

            ComprobarEstadoVinculacion();
            CargarConfiguracion();
        }

        private void CargarConfiguracion()
        {
            // 1. CARGAR DATOS GLOBALES (BBDD)
            TxtNombre.Text = DatabaseHelper.ObtenerConfiguracion("NombreSC", "");
            TxtApellido.Text = DatabaseHelper.ObtenerConfiguracion("ApellidoSC", "");
            TxtNombreEsposa.Text = DatabaseHelper.ObtenerConfiguracion("NombreEsposaSC", "");
            TxtApellidoEsposa.Text = DatabaseHelper.ObtenerConfiguracion("ApellidoEsposaSC", "");
            TxtNumCircuito.Text = DatabaseHelper.ObtenerConfiguracion("NumeroCircuito", "");
            TxtRutaArchivoCircuito.Text = DatabaseHelper.ObtenerConfiguracion("RutaArchivoCircuito", "");

            // 2. CARGAR DATOS LOCALES (Este PC)
            TxtRutaBaseDatos.Text = DatabaseHelper.RutaDirectorioDatos;

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
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
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

                MessageBox.Show("Toda la configuración se ha guardado correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar la configuración: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (dialog.ShowDialog() == true) TxtRutaArchivoCircuito.Text = dialog.FolderName;
        }

        private void BtnRutaDescargas_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta para los discursos descargados",
                InitialDirectory = string.IsNullOrEmpty(TxtRutaDescargas.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : TxtRutaDescargas.Text
            };
            if (dialog.ShowDialog() == true) TxtRutaDescargas.Text = dialog.FolderName;
        }

        private void BtnCambiarRuta_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta donde quieres guardar los datos de SCCosmos",
                InitialDirectory = DatabaseHelper.RutaDirectorioDatos
            };

            if (dialog.ShowDialog() == true)
            {
                string nuevaRuta = dialog.FolderName;
                try
                {
                    string bdAntigua = Path.Combine(DatabaseHelper.RutaDirectorioDatos, "sccosmos.db");
                    string bdNueva = Path.Combine(nuevaRuta, "sccosmos.db");

                    if (File.Exists(bdAntigua) && !File.Exists(bdNueva)) File.Copy(bdAntigua, bdNueva);

                    DatabaseHelper.RutaDirectorioDatos = nuevaRuta;
                    TxtRutaBaseDatos.Text = nuevaRuta;

                    MessageBox.Show("Ruta actualizada correctamente.\n\nEl programa leerá y escribirá los datos en esta nueva ubicación a partir de ahora.", "Sincronización", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Hubo un error al intentar cambiar la ruta:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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