using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;

namespace SCCosmos
{
    public partial class ConfiguracionPage : Page
    {
        public ConfiguracionPage()
        {
            InitializeComponent();

            ComprobarEstadoVinculacion();

            // Cargamos la ruta actual de la base de datos al abrir la pestaña
            TxtRutaBaseDatos.Text = DatabaseHelper.RutaDirectorioDatos;
        }

        private void ComprobarEstadoVinculacion()
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

            if (Directory.Exists(folderPath))
            {
                TxtTitulo.Text = "Cuenta de JW vinculada";
                TxtTitulo.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                IconoEstado.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));

                TxtDescripcion.Text = "Tu perfil exclusivo de Firefox ya está creado y se ha guardando tu sesión.";
                BtnVincularCorreo.Content = "Actualizar sesión";
            }
        }

        private void BtnVincularCorreo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                var service = FirefoxDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                var options = new FirefoxOptions();
                options.AddArgument("-profile");
                options.AddArgument(folderPath);
                options.AddArgument("-no-remote");

                MessageBox.Show(
                    "Se va a abrir una ventana de Firefox limpia.\n\n" +
                    "PASOS A SEGUIR:\n" +
                    "1. Inicia sesión en tu correo jwpub.org.\n" +
                    "2. Marca la casilla 'Mantener la sesión iniciada'.\n" +
                    "3. Cuando veas tu bandeja de entrada, cierra Firefox manualmente.\n\n" +
                    "Esto solo lo tendrás que hacer una vez.",
                    "Configuración de perfil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                IWebDriver driver = new FirefoxDriver(service, options);
                driver.Navigate().GoToUrl("https://mail.jwpub.org/owa/#path=/mail");

                ComprobarEstadoVinculacion();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al configurar el perfil de Firefox: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCambiarRuta_Click(object sender, RoutedEventArgs e)
        {
            // Usamos el selector de carpetas NATIVO de WPF en .NET 8 (no requiere Windows Forms)
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta donde quieres guardar los datos de SCCosmos",
                InitialDirectory = DatabaseHelper.RutaDirectorioDatos
            };

            if (dialog.ShowDialog() == true)
            {
                string nuevaRuta = dialog.FolderName; // Obtenemos la ruta seleccionada

                try
                {
                    string bdAntigua = Path.Combine(DatabaseHelper.RutaDirectorioDatos, "sccosmos.db");
                    string bdNueva = Path.Combine(nuevaRuta, "sccosmos.db");

                    // Si hay datos en la ruta antigua pero NO en la nueva, los copiamos automáticamente para no perder nada
                    if (File.Exists(bdAntigua) && !File.Exists(bdNueva))
                    {
                        File.Copy(bdAntigua, bdNueva);
                    }

                    // Actualizamos la propiedad (esto lo guarda en el AppData oculto de Windows)
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
    }
}