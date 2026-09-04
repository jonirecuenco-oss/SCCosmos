using iText.Forms;
using iText.Kernel.Pdf;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;

namespace SCCosmos
{
    public partial class CalendarioPage : Page
    {
        public ObservableCollection<SemanaCalendario> ColeccionSemanas { get; set; }
        private ICollectionView _vistaAgrupada;

        private DateTime _fechaLimiteAnterior;
        private DateTime _fechaLimitePosterior;
        private ScrollViewer _scrollPadreFantasma;

        public CalendarioPage()
        {
            InitializeComponent();

            ColeccionSemanas = new ObservableCollection<SemanaCalendario>();
            _vistaAgrupada = CollectionViewSource.GetDefaultView(ColeccionSemanas);

            _vistaAgrupada.SortDescriptions.Add(new SortDescription("FechaMartes", ListSortDirection.Ascending));
            _vistaAgrupada.SortDescriptions.Add(new SortDescription("Id", ListSortDirection.Ascending));
            _vistaAgrupada.GroupDescriptions.Add(new PropertyGroupDescription("Banner"));

            InicializarCalendarioRangoFijo();

            TablaRuta.ItemsSource = _vistaAgrupada;

            this.Loaded += CalendarioPage_Loaded;
            this.Unloaded += CalendarioPage_Unloaded;
        }

        private void InicializarCalendarioRangoFijo()
        {
            DateTime hoy = DateTime.Today;
            DateTime martesActual = hoy;
            while (martesActual.DayOfWeek != DayOfWeek.Tuesday)
            {
                martesActual = martesActual.AddDays(-1);
            }

            // REDUCIDO DE 52 A 26 SEMANAS (6 MESES)
            _fechaLimiteAnterior = martesActual.AddDays(-26 * 7);
            _fechaLimitePosterior = martesActual.AddDays(26 * 7);

            DateTime bucleFecha = _fechaLimiteAnterior;
            while (bucleFecha <= _fechaLimitePosterior)
            {
                InsertarSemanasDesdeDB(bucleFecha, addToBottom: true);
                bucleFecha = bucleFecha.AddDays(7);
            }
        }

        private void InsertarSemanasDesdeDB(DateTime fecha, bool addToBottom)
        {
            string isoId = fecha.ToString("yyyy-MM-dd");
            List<SemanaCalendario> semanasEnEstaFecha = DatabaseHelper.ObtenerTodasSemanasPorFecha(isoId);

            if (semanasEnEstaFecha.Count == 0)
            {
                var semanaVacia = new SemanaCalendario
                {
                    FechaMartes = fecha,
                    Actividad = "",
                    Congregacion = "",
                    Acompanante = "",
                    Tipo = "",
                    Alojamiento = ""
                };
                semanasEnEstaFecha.Add(semanaVacia);
            }

            for (int i = 0; i < semanasEnEstaFecha.Count; i++)
            {
                var s = semanasEnEstaFecha[i];
                if (i > 0) s.EsClonada = Visibility.Visible;

                s = delayEstado(s);

                if (addToBottom) ColeccionSemanas.Add(s);
                else ColeccionSemanas.Insert(0, s);
            }
        }

        private void BtnSolaparActividad_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var semanaBase = boton.DataContext as SemanaCalendario;

            if (semanaBase != null)
            {
                var nuevaSemanaSolapada = new SemanaCalendario
                {
                    FechaMartes = semanaBase.FechaMartes,
                    Actividad = "",
                    Congregacion = "",
                    Acompanante = "",
                    Tipo = "",
                    Alojamiento = "",
                    EsClonada = Visibility.Visible
                };

                DatabaseHelper.GuardarSemana(nuevaSemanaSolapada);
                RecargarFechaEspecifica(semanaBase.FechaMartes);

                var ultimaInsertada = ColeccionSemanas.LastOrDefault(s => s.FechaMartes == semanaBase.FechaMartes);
                if (ultimaInsertada != null)
                {
                    FormularioSemana ventanaForm = new FormularioSemana(ultimaInsertada);
                    ventanaForm.Owner = Window.GetWindow(this);

                    if (ventanaForm.ShowDialog() == true)
                    {
                        DatabaseHelper.GuardarSemana(ultimaInsertada);
                        _vistaAgrupada.Refresh();
                    }
                    else
                    {
                        DatabaseHelper.EliminarSemanaCalendario(ultimaInsertada.Id);
                        RecargarFechaEspecifica(semanaBase.FechaMartes);
                    }
                }
            }
        }

        private void BtnEliminarActividad_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var semana = boton.DataContext as SemanaCalendario;

            if (semana != null)
            {
                if (MessageBox.Show("¿Seguro que deseas eliminar esta actividad paralela de esta semana?", "Eliminar Actividad", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    DatabaseHelper.EliminarSemanaCalendario(semana.Id);
                    ColeccionSemanas.Remove(semana);
                    _vistaAgrupada.Refresh();
                }
            }
        }

        private void RecargarFechaEspecifica(DateTime fechaTarget)
        {
            var aBorrar = ColeccionSemanas.Where(s => s.FechaMartes == fechaTarget).ToList();
            foreach (var item in aBorrar) ColeccionSemanas.Remove(item);

            InsertarSemanasDesdeDB(fechaTarget, addToBottom: true);
            _vistaAgrupada.Refresh();
        }

        private SemanaCalendario delayEstado(SemanaCalendario s) => s;

        private void BtnVerVisita_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var semanaSeleccionada = boton.DataContext as SemanaCalendario;
            if (semanaSeleccionada == null) return;

            string congreCodigo = DatabaseHelper.ObtenerCodigoCongregacion(semanaSeleccionada.Congregacion);
            if (!string.IsNullOrEmpty(congreCodigo))
            {
                VisitasPage.CodigoPendiente = congreCodigo;
                VisitasPage.NombrePendiente = semanaSeleccionada.Congregacion;
                VisitasPage.MesPendiente = semanaSeleccionada.FechaMartes.ToString("yyyy-MM");

                var ventanaPrincipal = Application.Current.MainWindow as MainWindow;
                if (ventanaPrincipal != null)
                {
                    ventanaPrincipal.MenuPrincipal.Navigate(typeof(VisitasPage));
                }
            }
        }

        private ScrollViewer ObtenerScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer viewer) return viewer;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                var result = ObtenerScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void BtnCargarPasado_Click(object sender, RoutedEventArgs e)
        {
            var scrollViewer = ObtenerScrollViewer(TablaRuta);
            double offsetAnterior = scrollViewer != null ? scrollViewer.VerticalOffset : 0;
            double alturaTotalAnterior = scrollViewer != null ? scrollViewer.ExtentHeight : 0; // Guardamos la altura antes de insertar

            for (int i = 0; i < 26; i++)
            {
                _fechaLimiteAnterior = _fechaLimiteAnterior.AddDays(-7);
                InsertarSemanasDesdeDB(_fechaLimiteAnterior, addToBottom: false);
            }
            _vistaAgrupada.Refresh();

            if (scrollViewer != null)
            {
                // Usamos DispatcherPriority.Loaded para asegurarnos de que la tabla ya se ha dibujado antes de medirla
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    TablaRuta.UpdateLayout();
                    // Calculamos exactamente cuánto ha crecido la tabla "por arriba"
                    double diferenciaAltura = scrollViewer.ExtentHeight - alturaTotalAnterior;
                    // Desplazamos la vista exactamente esa misma cantidad hacia abajo para que quede congelada en tu posición
                    scrollViewer.ScrollToVerticalOffset(offsetAnterior + diferenciaAltura);
                }));
            }
        }

        private void BtnCargarFuturo_Click(object sender, RoutedEventArgs e)
        {
            // REDUCIDO DE 52 A 26 SEMANAS
            for (int i = 0; i < 26; i++)
            {
                _fechaLimitePosterior = _fechaLimitePosterior.AddDays(7);
                InsertarSemanasDesdeDB(_fechaLimitePosterior, addToBottom: true);
            }
            _vistaAgrupada.Refresh();
        }

        private void CalendarioPage_Loaded(object sender, RoutedEventArgs e)
        {
            DependencyObject parent = System.Windows.Media.VisualTreeHelper.GetParent(this);
            while (parent != null)
            {
                if (parent is ScrollViewer scrollPadre)
                {
                    _scrollPadreFantasma = scrollPadre;
                    _scrollPadreFantasma.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    break;
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                int indiceActual = -1;
                DateTime martesActual = DateTime.Today;
                while (martesActual.DayOfWeek != DayOfWeek.Tuesday) martesActual = martesActual.AddDays(-1);

                for (int i = 0; i < ColeccionSemanas.Count; i++)
                {
                    if (ColeccionSemanas[i].FechaMartes.Year == martesActual.Year &&
                        ColeccionSemanas[i].FechaMartes.Month == martesActual.Month)
                    {
                        indiceActual = i;
                        break;
                    }
                }

                if (indiceActual != -1)
                {
                    int indiceConMargen = Math.Max(0, indiceActual - 1);
                    TablaRuta.ScrollIntoView(ColeccionSemanas[ColeccionSemanas.Count - 1]);
                    TablaRuta.UpdateLayout();
                    TablaRuta.ScrollIntoView(ColeccionSemanas[indiceConMargen]);
                }
            }));
        }

        private void CalendarioPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_scrollPadreFantasma != null)
            {
                _scrollPadreFantasma.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var semanaSeleccionada = boton.DataContext as SemanaCalendario;

            if (semanaSeleccionada != null)
            {
                FormularioSemana ventanaForm = new FormularioSemana(semanaSeleccionada);
                ventanaForm.Owner = Window.GetWindow(this);

                if (ventanaForm.ShowDialog() == true)
                {
                    DatabaseHelper.GuardarSemana(semanaSeleccionada);
                    _vistaAgrupada.Refresh();
                }
            }
        }

        private void BtnImportar_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var semanaSeleccionada = boton.DataContext as SemanaCalendario;

            if (semanaSeleccionada == null) return;
            if (string.IsNullOrEmpty(semanaSeleccionada.Congregacion))
            {
                MessageBox.Show("Debes asignar una congregación a esta semana antes de importar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string congreCodigo = DatabaseHelper.ObtenerCodigoCongregacion(semanaSeleccionada.Congregacion);

            ImportarArchivosActividad ventanaArchivos = new ImportarArchivosActividad(congreCodigo, semanaSeleccionada.Congregacion, semanaSeleccionada.TieneDatosImportados);
            ventanaArchivos.Owner = Window.GetWindow(this);

            if (ventanaArchivos.ShowDialog() == true)
            {
                semanaSeleccionada.TieneDatosImportados = true;
                DatabaseHelper.GuardarSemana(semanaSeleccionada);
                _vistaAgrupada.Refresh();
            }
        }

        private async void BtnEnviarS302_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var semanaSeleccionada = boton.DataContext as SemanaCalendario;
            if (semanaSeleccionada == null) return;

            var archivosS302 = DatabaseHelper.ObtenerDocumentos("Aviso de la visita");

            if (archivosS302 == null || archivosS302.Count == 0)
            {
                MessageBox.Show("No has subido ningún archivo en el apartado Documentación > Aviso de la visita.\n\nPor favor, sube el archivo S-302 maestro antes de continuar.", "Envío bloqueado", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string congreNombre = semanaSeleccionada.Congregacion;
            DateTime fechaMartes = semanaSeleccionada.FechaMartes;
            string rutaPdfOriginal = archivosS302[0].RutaLocal;
            string rutaPdfRelleno = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"S-302_{congreNombre}.pdf");

            // ======================================================================
            // 1. RECOPILAR DATOS PARA LOS CAMPOS DEL S-302
            // ======================================================================
            string nombreSC = DatabaseHelper.ObtenerConfiguracion("NombreSC", "Jonatán");
            string apellidoSC = DatabaseHelper.ObtenerConfiguracion("ApellidoSC", "Recuenco");
            string nombreCompletoSC = $"{nombreSC} {apellidoSC}".Trim();

            string nombrePrincipalSCS = "";
            string apellidoPrincipalSCS = "";

            if (!string.IsNullOrEmpty(semanaSeleccionada.Acompanante))
            {
                string rawName = semanaSeleccionada.Acompanante;
                string[] partesY = rawName.Split(new[] { " y " }, StringSplitOptions.None);
                string maridoFull = partesY[0].Trim();

                if (maridoFull.Contains(","))
                {
                    var partesComa = maridoFull.Split(',');
                    apellidoPrincipalSCS = partesComa[0].Trim();
                    nombrePrincipalSCS = partesComa[1].Trim();
                }
                else
                {
                    nombrePrincipalSCS = maridoFull;
                }
            }
            string nombreComunSCS = $"{nombrePrincipalSCS} {apellidoPrincipalSCS}".Trim();

            string text2Value = nombreCompletoSC;
            if (semanaSeleccionada.Tipo != null &&
               (semanaSeleccionada.Tipo.Contains("SCS", StringComparison.OrdinalIgnoreCase) ||
                semanaSeleccionada.Tipo.Equals("Visita de SCS", StringComparison.OrdinalIgnoreCase)))
            {
                text2Value = string.IsNullOrWhiteSpace(nombreComunSCS) ? "(SCS)" : $"{nombreComunSCS} (SCS)";
            }

            string text4Value = nombreCompletoSC;

            DateTime fechaInicio = fechaMartes;
            DateTime fechaFin = fechaInicio.AddDays(5);
            string textoFechas = $"Del {fechaInicio.ToString("dd-MMM-yy").ToLower()} al {fechaFin.ToString("dd-MMM-yy").ToLower()}";

            // ======================================================================
            // 2. RELLENAR EL PDF ANTES DE ABRIR EL POPUP
            // ======================================================================
            try
            {
                using (PdfDocument pdfDoc = new PdfDocument(new PdfReader(rutaPdfOriginal), new PdfWriter(rutaPdfRelleno)))
                {
                    PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, true);
                    if (form != null)
                    {
                        form.GetField("Text1")?.SetValue(congreNombre);
                        form.GetField("Text2")?.SetValue(text2Value);
                        form.GetField("Text3")?.SetValue(textoFechas);
                        form.GetField("Text4")?.SetValue(text4Value);
                        form.FlattenFields();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo generar el PDF S-302.\nAsegúrate de que no está abierto en otro programa.\n\nError: {ex.Message}", "Error de PDF", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var archivosParaPreview = new List<ArchivoDocumento>
            {
                new ArchivoDocumento
                {
                    Id = archivosS302[0].Id,
                    NombreArchivo = $"S-302_{congreNombre}.pdf",
                    RutaLocal = rutaPdfRelleno,
                    Categoria = archivosS302[0].Categoria
                }
            };

            var popup = new ConfirmarEnvioWindow($"Enviar Aviso: {congreNombre}", semanaSeleccionada.S302Enviado, archivosParaPreview);
            popup.Owner = Window.GetWindow(this);

            bool? procederEnvio = popup.ShowDialog();

            if (popup.EstadoModificadoManual && procederEnvio != true)
            {
                semanaSeleccionada.S302Enviado = popup.EstadoEnviadoActual;
                DatabaseHelper.GuardarSemana(semanaSeleccionada);
                _vistaAgrupada.Refresh();
            }

            if (procederEnvio != true) return;

            // ======================================================================
            // 4. PROCEDER AL ENVÍO AUTOMÁTICO (SELENIUM)
            // ======================================================================
            string congreCodigo = DatabaseHelper.ObtenerCodigoCongregacion(congreNombre);
            string correoCongregacion = $"CONG601{congreCodigo}@jwpub.org";
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

            string asuntoEmailS302 = DatabaseHelper.ObtenerConfiguracion("Asunto_Aviso de la visita", "Aviso de la visita");
            string mensajeHtmlS302 = DatabaseHelper.ObtenerConfiguracion("Email_Aviso de la visita", "Queridos compañeros,<br><br>Os dejo adjunto el formulario S-302 con la fecha de la próxima visita.");

            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show("Debes ir a 'Configuración' y vincular tu cuenta de correo primero.", "Perfil no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

                        IWebElement btnNuevo = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[contains(@title, 'Escribir un mensaje nuevo') or .//span[text()='Nuevo']]"));
                            foreach (var btn in botones) { if (btn.Displayed && btn.Enabled) return btn; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnNuevo);

                        IWebElement inputCorreo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@aria-label='Para' and @role='textbox']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        System.Threading.Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputCorreo);

                        inputCorreo.SendKeys(correoCongregacion);
                        System.Threading.Thread.Sleep(500);
                        inputCorreo.SendKeys(Keys.Return);
                        System.Threading.Thread.Sleep(200);
                        inputCorreo.SendKeys(Keys.Tab);

                        IWebElement inputAsunto = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[contains(@aria-label, 'Asunto') or @placeholder='Agregar un asunto']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputAsunto);
                        inputAsunto.SendKeys(asuntoEmailS302);

                        IWebElement inputArchivo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@type='file']"));
                            return inputs.Count > 0 ? inputs[0] : null;
                        });

                        inputArchivo.SendKeys(rutaPdfRelleno);
                        System.Threading.Thread.Sleep(1000);

                        WebDriverWait waitSubida = new WebDriverWait(driver, TimeSpan.FromSeconds(90));

                        string nombreCortoS302 = Path.GetFileNameWithoutExtension(rutaPdfRelleno);
                        if (nombreCortoS302.Length > 15) nombreCortoS302 = nombreCortoS302.Substring(0, 15);
                        try { waitSubida.Until(d => d.PageSource.Contains(nombreCortoS302)); } catch { }

                        try
                        {
                            waitSubida.Until(d => {
                                var spinners = d.FindElements(By.XPath("//div[contains(@class, 'spinnerAnimation')]"));
                                if (spinners.Count == 0)
                                {
                                    System.Threading.Thread.Sleep(1000);
                                    return d.FindElements(By.XPath("//div[contains(@class, 'spinnerAnimation')]")).Count == 0;
                                }
                                return false;
                            });
                        }
                        catch { }

                        System.Threading.Thread.Sleep(2000);

                        IWebElement inputCuerpo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//div[@role='textbox' and @aria-label='Cuerpo del mensaje']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });

                        ((IJavaScriptExecutor)driver).ExecuteScript(
                            "arguments[0].focus(); document.execCommand('insertHTML', false, arguments[1]);",
                            inputCuerpo, mensajeHtmlS302);

                        System.Threading.Thread.Sleep(1000);

                        IWebElement btnEnviar = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[@aria-label='Enviar' or @title='Enviar']"));
                            foreach (var b in botones) { if (b.Displayed && b.Enabled) return b; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);

                        System.Threading.Thread.Sleep(4000);
                    }
                    finally
                    {
                        if (driver != null) driver.Quit();
                    }
                });

                semanaSeleccionada.S302Enviado = true;
                DatabaseHelper.GuardarSemana(semanaSeleccionada);
                _vistaAgrupada.Refresh();

                MessageBox.Show("S-302 generado y enviado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hubo un error al procesar el archivo o abrir el navegador:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OverlayCargando.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnEnviarDoc_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var semanaSeleccionada = boton.DataContext as SemanaCalendario;
            if (semanaSeleccionada == null) return;

            string categoriaBase = "";
            if (semanaSeleccionada.Tipo == "Visita SC") categoriaBase = "Documentación";
            else if (semanaSeleccionada.Tipo == "Visita (con SCS)") categoriaBase = "Documentación (con SCS)";
            else if (semanaSeleccionada.Tipo == "Visita (de pastoreo)") categoriaBase = "Documentación (con visita de pastoreo)";

            string subCategoria = "Sin alojamiento";
            if (!string.IsNullOrEmpty(semanaSeleccionada.Alojamiento))
            {
                string aloj = semanaSeleccionada.Alojamiento.ToLower();

                if (categoriaBase == "Documentación")
                {
                    if (aloj.Contains("sin")) subCategoria = "Sin alojamiento";
                    else subCategoria = "Con alojamiento";
                }
                else
                {
                    if (aloj.Contains("sin")) subCategoria = "Sin alojamiento";
                    else if (aloj.Contains("ambos")) subCategoria = "Alojamiento (ambos)";
                    else if (aloj.Contains("solo acomp") || aloj.Contains("acompañante")) subCategoria = "Solo acomp";
                    else if (aloj.Contains("solo yo")) subCategoria = "Aloj (solo yo)";
                    else subCategoria = "Con alojamiento";
                }
            }

            string tagBusqueda = $"{categoriaBase}|{subCategoria}";
            var archivosOriginales = DatabaseHelper.ObtenerDocumentos(tagBusqueda);

            if ((archivosOriginales == null || archivosOriginales.Count == 0) && subCategoria.Contains("Alojamiento"))
            {
                subCategoria = "Con alojamiento";
                tagBusqueda = $"{categoriaBase}|{subCategoria}";
                archivosOriginales = DatabaseHelper.ObtenerDocumentos(tagBusqueda);
            }

            if (archivosOriginales == null || archivosOriginales.Count == 0)
            {
                MessageBox.Show($"Faltan documentos en:\n- {categoriaBase}\n- {subCategoria}\n\nSúbelos en el apartado CORRESPONDENCIA > Visitas a las congregaciones.", "Faltan documentos", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string nombrePrincipal = "";
            string apellidoPrincipal = "";
            string nombreEsposa = "";

            if (!string.IsNullOrEmpty(semanaSeleccionada.Acompanante))
            {
                string rawName = semanaSeleccionada.Acompanante;
                string[] partesY = rawName.Split(new[] { " y " }, StringSplitOptions.None);

                string maridoFull = partesY[0].Trim();

                if (maridoFull.Contains(","))
                {
                    var partesComa = maridoFull.Split(',');
                    apellidoPrincipal = partesComa[0].Trim();
                    nombrePrincipal = partesComa[1].Trim();
                }
                else
                {
                    nombrePrincipal = maridoFull;
                }

                if (partesY.Length > 1)
                {
                    string esposaFull = partesY[1].Trim();
                    if (esposaFull.Contains(","))
                        nombreEsposa = esposaFull.Split(',')[1].Trim();
                    else
                        nombreEsposa = esposaFull;
                }
            }

            string varNombreSCS = DatabaseHelper.ObtenerConfiguracion("VarSCS", "$_NOMBRE_SCS");
            string varApellidoSCS = DatabaseHelper.ObtenerConfiguracion("VarApSCS", "$_APELLIDO_SCS");
            string varEspSCS = DatabaseHelper.ObtenerConfiguracion("VarEsposaSCS", "$_ESPOSA_SCS");

            string varNombrePast = DatabaseHelper.ObtenerConfiguracion("VarPastoreo", "$_NOMBRE_VISITA");
            string varApellidoPast = DatabaseHelper.ObtenerConfiguracion("VarApPastoreo", "$_APELLIDO_VISITA");
            string varEspPast = DatabaseHelper.ObtenerConfiguracion("VarEsposaPastoreo", "$_ESPOSA_VISITA");

            string asuntoEmailDoc = DatabaseHelper.ObtenerConfiguracion("Asunto_" + categoriaBase, "Documentación de la visita");
            string mensajeHtmlDoc = DatabaseHelper.ObtenerConfiguracion("Email_" + categoriaBase, "Queridos hermanos,<br><br>Adjuntamos la documentación.");

            string carpetaTemp = Path.Combine(Path.GetTempPath(), "SCCosmos_Docs_" + semanaSeleccionada.Congregacion);
            if (Directory.Exists(carpetaTemp)) Directory.Delete(carpetaTemp, true);
            Directory.CreateDirectory(carpetaTemp);

            List<ArchivoDocumento> archivosProcesados = new List<ArchivoDocumento>();

            foreach (var arch in archivosOriginales)
            {
                string rutaNueva = Path.Combine(carpetaTemp, arch.NombreArchivo);
                File.Copy(arch.RutaLocal, rutaNueva, true);

                if (Path.GetExtension(rutaNueva).ToLower() == ".docx")
                {
                    try
                    {
                        using (var document = Xceed.Words.NET.DocX.Load(rutaNueva))
                        {
                            if (semanaSeleccionada.Tipo == "Visita (con SCS)")
                            {
                                document.ReplaceText(varNombreSCS, nombrePrincipal);
                                document.ReplaceText(varApellidoSCS, apellidoPrincipal);
                                document.ReplaceText(varEspSCS, string.IsNullOrEmpty(nombreEsposa) ? "" : nombreEsposa);
                            }
                            else if (semanaSeleccionada.Tipo == "Visita (de pastoreo)")
                            {
                                document.ReplaceText(varNombrePast, nombrePrincipal);
                                document.ReplaceText(varApellidoPast, apellidoPrincipal);
                                document.ReplaceText(varEspPast, string.IsNullOrEmpty(nombreEsposa) ? "" : nombreEsposa);
                            }
                            document.Save();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error modificando archivo {arch.NombreArchivo}.\n{ex.Message}");
                    }
                }

                rutaNueva = DocumentoHelper.ConvertirAPdfSiEsWord(rutaNueva);
                string nombreFinalArchivo = Path.GetFileName(rutaNueva);

                archivosProcesados.Add(new ArchivoDocumento
                {
                    Id = arch.Id,
                    NombreArchivo = nombreFinalArchivo,
                    RutaLocal = rutaNueva,
                    Categoria = arch.Categoria
                });
            }

            var popup = new ConfirmarEnvioWindow($"Doc: {semanaSeleccionada.Congregacion}", semanaSeleccionada.DocEnviada, archivosProcesados);
            popup.Owner = Window.GetWindow(this);

            bool? procederEnvio = popup.ShowDialog();

            if (popup.EstadoModificadoManual && procederEnvio != true)
            {
                semanaSeleccionada.DocEnviada = popup.EstadoEnviadoActual;
                DatabaseHelper.GuardarSemana(semanaSeleccionada);
                _vistaAgrupada.Refresh();
            }

            if (procederEnvio == true)
            {
                string congreNombre = semanaSeleccionada.Congregacion;
                string congreCodigo = DatabaseHelper.ObtenerCodigoCongregacion(congreNombre);
                string correoCongregacion = $"CONG601{congreCodigo}@jwpub.org";
                string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

                if (!Directory.Exists(folderPath))
                {
                    MessageBox.Show("Debes ir a 'Configuración' y vincular tu cuenta de correo primero.", "Perfil no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

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

                            IWebElement btnNuevo = wait.Until(d => {
                                var botones = d.FindElements(By.XPath("//button[contains(@title, 'Escribir un mensaje nuevo') or .//span[text()='Nuevo']]"));
                                foreach (var btn in botones) { if (btn.Displayed && btn.Enabled) return btn; }
                                return null;
                            });
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnNuevo);

                            IWebElement inputCorreo = wait.Until(d => {
                                var inputs = d.FindElements(By.XPath("//input[@aria-label='Para' and @role='textbox']"));
                                foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                                return null;
                            });

                            System.Threading.Thread.Sleep(500);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputCorreo);

                            inputCorreo.SendKeys(correoCongregacion);
                            System.Threading.Thread.Sleep(500);
                            inputCorreo.SendKeys(Keys.Return);
                            System.Threading.Thread.Sleep(200);
                            inputCorreo.SendKeys(Keys.Tab);

                            IWebElement inputAsunto = wait.Until(d => {
                                var inputs = d.FindElements(By.XPath("//input[contains(@aria-label, 'Asunto') or @placeholder='Agregar un asunto']"));
                                foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                                return null;
                            });
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputAsunto);
                            inputAsunto.SendKeys(asuntoEmailDoc);

                            IWebElement inputArchivo = wait.Until(d => {
                                var inputs = d.FindElements(By.XPath("//input[@type='file']"));
                                return inputs.Count > 0 ? inputs[0] : null;
                            });

                            // SUBIMOS TODOS LOS ARCHIVOS
                            string rutasUnidas = string.Join("\n", archivosProcesados.Select(a => a.RutaLocal));
                            inputArchivo.SendKeys(rutasUnidas);

                            System.Threading.Thread.Sleep(1000 * archivosProcesados.Count);

                            WebDriverWait waitSubida = new WebDriverWait(driver, TimeSpan.FromSeconds(90));

                            foreach (var arch in archivosProcesados)
                            {
                                string nombreCorto = Path.GetFileNameWithoutExtension(arch.NombreArchivo);
                                if (nombreCorto.Length > 15) nombreCorto = nombreCorto.Substring(0, 15);
                                try { waitSubida.Until(d => d.PageSource.Contains(nombreCorto)); } catch { }
                            }

                            // Bucle Anti-Parpadeo
                            try
                            {
                                waitSubida.Until(d => {
                                    var spinners = d.FindElements(By.XPath("//div[contains(@class, 'spinnerAnimation')]"));
                                    if (spinners.Count == 0)
                                    {
                                        System.Threading.Thread.Sleep(1000);
                                        return d.FindElements(By.XPath("//div[contains(@class, 'spinnerAnimation')]")).Count == 0;
                                    }
                                    return false;
                                });
                            }
                            catch { }

                            System.Threading.Thread.Sleep(2000);

                            IWebElement inputCuerpo = wait.Until(d => {
                                var inputs = d.FindElements(By.XPath("//div[@role='textbox' and @aria-label='Cuerpo del mensaje']"));
                                foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                                return null;
                            });

                            ((IJavaScriptExecutor)driver).ExecuteScript(
                                "arguments[0].focus(); document.execCommand('insertHTML', false, arguments[1]);",
                                inputCuerpo, mensajeHtmlDoc);

                            System.Threading.Thread.Sleep(1000);

                            IWebElement btnEnviar = wait.Until(d => {
                                var botones = d.FindElements(By.XPath("//button[@aria-label='Enviar' or @title='Enviar']"));
                                foreach (var b in botones) { if (b.Displayed && b.Enabled) return b; }
                                return null;
                            });
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);

                            System.Threading.Thread.Sleep(4000);
                        }
                        finally
                        {
                            if (driver != null) driver.Quit();
                        }
                    });

                    semanaSeleccionada.DocEnviada = true;
                    DatabaseHelper.GuardarSemana(semanaSeleccionada);
                    _vistaAgrupada.Refresh();

                    MessageBox.Show("Documentación procesada y enviada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Hubo un error al procesar el archivo o abrir el navegador:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    OverlayCargando.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void BtnEnviarRecordatorio_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var semana = boton.DataContext as SemanaCalendario;

            if (semana == null) return;

            var archivosVacios = new List<ArchivoDocumento>();
            var popup = new ConfirmarEnvioWindow($"Recordatorio Sec. {semana.SeccionAsamblea}", semana.RecordatorioEnviado, archivosVacios);
            popup.Owner = Window.GetWindow(this);

            bool? procederEnvio = popup.ShowDialog();

            if (popup.EstadoModificadoManual && procederEnvio != true)
            {
                semana.RecordatorioEnviado = popup.EstadoEnviadoActual;
                DatabaseHelper.GuardarSemana(semana);
                _vistaAgrupada.Refresh();
            }

            if (procederEnvio != true) return;

            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show("Debes ir a 'Configuración' y vincular tu cuenta de correo primero.", "Perfil no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var todasLasCongregaciones = DatabaseHelper.ObtenerCongregaciones();

            var correosSeccion = todasLasCongregaciones
                .Where(c => c.NombreSeccion == semana.SeccionAsamblea && !string.IsNullOrWhiteSpace(c.Email))
                .Select(c => c.Email.Trim())
                .ToList();

            if (correosSeccion.Count == 0)
            {
                MessageBox.Show($"No se han encontrado correos electrónicos registrados para las congregaciones de la Sección {semana.SeccionAsamblea}.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string destinatarios = string.Join(";", correosSeccion);
            string asunto = DatabaseHelper.ObtenerConfiguracion("Asunto_Recordatorio de asamblea de circuito", "Recordatorio de asamblea de circuito");
            string cuerpoHtml = DatabaseHelper.ObtenerConfiguracion("Email_Recordatorio de asamblea de circuito", "Queridos hermanos...");

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

                        IWebElement btnNuevo = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[contains(@title, 'Escribir un mensaje nuevo') or .//span[text()='Nuevo']]"));
                            foreach (var btn in botones) { if (btn.Displayed && btn.Enabled) return btn; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnNuevo);

                        IWebElement inputCorreo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@aria-label='Para' and @role='textbox']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });

                        System.Threading.Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputCorreo);

                        inputCorreo.SendKeys(destinatarios);
                        System.Threading.Thread.Sleep(500);
                        inputCorreo.SendKeys(Keys.Return);
                        System.Threading.Thread.Sleep(200);
                        inputCorreo.SendKeys(Keys.Tab);

                        IWebElement inputAsunto = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[contains(@aria-label, 'Asunto') or @placeholder='Agregar un asunto']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputAsunto);
                        inputAsunto.SendKeys(asunto);


                        IWebElement inputCuerpo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//div[@role='textbox' and @aria-label='Cuerpo del mensaje']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });

                        ((IJavaScriptExecutor)driver).ExecuteScript(
                            "arguments[0].focus(); document.execCommand('insertHTML', false, arguments[1]);",
                            inputCuerpo, cuerpoHtml);

                        System.Threading.Thread.Sleep(1000);

                        IWebElement btnEnviar = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[@aria-label='Enviar' or @title='Enviar']"));
                            foreach (var b in botones) { if (b.Displayed && b.Enabled) return b; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);

                        System.Threading.Thread.Sleep(4000);
                    }
                    finally
                    {
                        if (driver != null) driver.Quit();
                    }
                });

                semana.RecordatorioEnviado = true;
                DatabaseHelper.GuardarSemana(semana);
                _vistaAgrupada.Refresh();

                MessageBox.Show("Recordatorio procesado y enviado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hubo un error al procesar el archivo o abrir el navegador:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OverlayCargando.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnConfigurarCiclo_Click(object sender, RoutedEventArgs e)
        {
            CicloVisitasWindow ventanaCiclo = new CicloVisitasWindow();
            ventanaCiclo.Owner = Window.GetWindow(this);
            ventanaCiclo.ShowDialog();
        }

        // Función que salta al hacer clic en cualquier parte de la fila
        private void FilaSemana_Click(object sender, MouseButtonEventArgs e)
        {
            // CORTAFUEGOS: Si el usuario ha hecho clic justo encima de un botón (como enviar S-302, etc), 
            // ignoramos el evento de la fila para que no se abra la ventana de edición por accidente.
            if (e.OriginalSource is DependencyObject source)
            {
                var esBotonEstandar = FindParent<Button>(source) != null;
                var esBotonWpfUi = FindParent<Wpf.Ui.Controls.Button>(source) != null;

                if (esBotonEstandar || esBotonWpfUi) return;
            }

            var row = sender as DataGridRow;
            var semanaSeleccionada = row?.DataContext as SemanaCalendario;

            if (semanaSeleccionada != null)
            {
                FormularioSemana ventanaForm = new FormularioSemana(semanaSeleccionada);
                ventanaForm.Owner = Window.GetWindow(this);

                if (ventanaForm.ShowDialog() == true)
                {
                    DatabaseHelper.GuardarSemana(semanaSeleccionada);
                    _vistaAgrupada.Refresh();
                }
            }
        }

        // Función auxiliar para saber qué es lo que hay debajo del cursor del ratón (útil para el cortafuegos)
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            return parent ?? FindParent<T>(parentObject);
        }

    }
}