using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using Wpf.Ui.Controls;

// LIBRERÍAS DE SELENIUM
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace SCCosmos
{
    public partial class EnviarMensajeWindow : FluentWindow
    {
        public ObservableCollection<CongregacionCheckItem> ListaCongregaciones { get; set; }
        public ObservableCollection<string> ArchivosAdjuntos { get; set; }

        public EnviarMensajeWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            ArchivosAdjuntos = new ObservableCollection<string>();
            CargarCongregaciones();
        }

        private void CargarCongregaciones()
        {
            var congregaciones = DatabaseHelper.ObtenerCongregaciones();

            ListaCongregaciones = new ObservableCollection<CongregacionCheckItem>(
                congregaciones.Select(c => new CongregacionCheckItem
                {
                    Nombre = c.Nombre,
                    Email = c.Email,
                    Seccion = string.IsNullOrEmpty(c.NombreSeccion) ? "Sin Sección" : c.NombreSeccion
                })
            );

            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(ListaCongregaciones);
            PropertyGroupDescription groupDescription = new PropertyGroupDescription("Seccion");
            view.GroupDescriptions.Add(groupDescription);
        }

        private void BtnAdjuntar_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Multiselect = true, Title = "Seleccionar archivos adjuntos" };
            if (ofd.ShowDialog() == true)
            {
                foreach (var file in ofd.FileNames)
                {
                    if (!ArchivosAdjuntos.Contains(file))
                        ArchivosAdjuntos.Add(file);
                }
            }
        }

        private void BtnEliminarAdjunto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string archivo)
            {
                ArchivosAdjuntos.Remove(archivo);
            }
        }

        private void GrupoCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox chk && chk.DataContext is CollectionViewGroup grupo)
            {
                bool estado = chk.IsChecked ?? false;
                foreach (CongregacionCheckItem item in grupo.Items)
                {
                    item.IsSelected = estado;
                }
            }
        }

        private void BtnSeleccionarTodas_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in ListaCongregaciones) c.IsSelected = true;
        }

        private void BtnDeseleccionarTodas_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in ListaCongregaciones) c.IsSelected = false;
        }

        // =========================================================
        // ENVIAR POR FIREFOX (SELENIUM)
        // =========================================================
        private async void BtnEnviar_Click(object sender, RoutedEventArgs e)
        {
            var seleccionadas = ListaCongregaciones.Where(c => c.IsSelected).ToList();

            if (string.IsNullOrWhiteSpace(TxtAsunto.Text))
            {
                System.Windows.MessageBox.Show("Por favor, escribe un asunto para el correo.", "Aviso", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (seleccionadas.Count == 0)
            {
                System.Windows.MessageBox.Show("Debes seleccionar al menos una congregación destino.", "Aviso", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // DESTINATARIOS (Comentada la lista real, usando el fijo temporal)
            //string destinoFinal = "9JonatanR@jwpub.org";
            string destinoFinal = string.Join(";", seleccionadas.Select(c => c.Email).Where(email => !string.IsNullOrWhiteSpace(email)));

            string asuntoFinal = TxtAsunto.Text;
            string cuerpoFinal = TxtCuerpo.Text;
            List<string> adjuntosFinales = ArchivosAdjuntos.ToList();

            string folderPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

            if (!System.IO.Directory.Exists(folderPath))
            {
                System.Windows.MessageBox.Show("Debes ir a 'Configuración' y vincular tu cuenta de correo primero (Perfil de Firefox no encontrado).", "Perfil no encontrado", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Mostramos el velo negro de carga para que no se toque la ventana
            OverlayCargando.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    var service = FirefoxDriverService.CreateDefaultService();
                    service.HideCommandPromptWindow = true;

                    var options = new FirefoxOptions();
                    options.AddArgument("-profile");
                    options.AddArgument(folderPath);
                    options.AddArgument("-no-remote");

                    IWebDriver driver = new FirefoxDriver(service, options);

                    try
                    {
                        driver.Navigate().GoToUrl("https://mail.jwpub.org/owa/#path=/mail");
                        WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

                        // 1. Clic en "Nuevo Mensaje"
                        IWebElement btnNuevo = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[contains(@title, 'Escribir un mensaje nuevo') or .//span[text()='Nuevo']]"));
                            foreach (var btn in botones) { if (btn.Displayed && btn.Enabled) return btn; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnNuevo);

                        // 2. Destinatario
                        IWebElement inputCorreo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@aria-label='Para' and @role='textbox']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputCorreo);

                        inputCorreo.SendKeys(destinoFinal);
                        Thread.Sleep(500);
                        inputCorreo.SendKeys(Keys.Return);
                        Thread.Sleep(200);
                        inputCorreo.SendKeys(Keys.Tab);

                        // 3. Asunto
                        IWebElement inputAsunto = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[contains(@aria-label, 'Asunto') or @placeholder='Agregar un asunto']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputAsunto);
                        inputAsunto.SendKeys(asuntoFinal);

                        // 4. Subir Adjuntos (Uno a uno)
                        if (adjuntosFinales.Count > 0)
                        {
                            IWebElement inputArchivo = wait.Until(d => {
                                var inputs = d.FindElements(By.XPath("//input[@type='file']"));
                                return inputs.Count > 0 ? inputs[0] : null;
                            });

                            foreach (var rutaArchivo in adjuntosFinales)
                            {
                                inputArchivo.SendKeys(rutaArchivo);
                                // Pausa breve por si el archivo pesa para que OWA lo registre bien antes de subir el siguiente
                                Thread.Sleep(1000);
                            }
                        }

                        // 5. Cuerpo del mensaje
                        IWebElement inputCuerpo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//div[@role='textbox' and @aria-label='Cuerpo del mensaje']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });

                        // Reemplazar saltos de línea de C# a formato HTML si el usuario no usó etiquetas
                        string htmlProcesado = cuerpoFinal.Replace("\r\n", "<br>").Replace("\n", "<br>");

                        ((IJavaScriptExecutor)driver).ExecuteScript(
                            "arguments[0].focus(); document.execCommand('insertHTML', false, arguments[1]);",
                            inputCuerpo, htmlProcesado);

                        Thread.Sleep(1000);

                        // 6. Enviar
                        IWebElement btnEnviar = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[@aria-label='Enviar' or @title='Enviar']"));
                            foreach (var b in botones) { if (b.Displayed && b.Enabled) return b; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);

                        // Esperar 4 segundos para asegurar que el correo salió de la bandeja de salida antes de matar Firefox
                        Thread.Sleep(4000);
                    }
                    finally
                    {
                        if (driver != null) driver.Quit();
                    }
                });

                System.Windows.MessageBox.Show("El mensaje ha sido enviado correctamente por Firefox.", "Éxito", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                this.Close(); // Cierra la ventana tras el éxito
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Hubo un error al procesar la automatización de Firefox:\n\n{ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                OverlayCargando.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class CongregacionCheckItem : INotifyPropertyChanged
    {
        public string Nombre { get; set; }
        public string Email { get; set; }
        public string Seccion { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}