using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace SCCosmos
{
    public static class SincronizadorJWHub
    {
        public static List<PersonaExtraida> Sincronizar(string rutaPerfilFirefox)
        {
            var diccionarioExtraidos = new Dictionary<string, PersonaExtraida>();
            FirefoxOptions options = new FirefoxOptions();
            options.AddArgument("-profile");
            options.AddArgument(rutaPerfilFirefox);

            using (IWebDriver driver = new FirefoxDriver(options))
            {
                try
                {
                    driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(15);

                    driver.Navigate().GoToUrl("https://hub.jw.org/");
                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
                    wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));

                    Thread.Sleep(3000);

                    var btnPersonas = wait.Until(d => d.FindElement(By.XPath("//a[contains(@href, 'congregation-persons') or contains(text(), 'Personas de la congregación')]")));
                    btnPersonas.Click();
                    Thread.Sleep(2000);

                    // 1. EXTRAER PRECURSORES REGULARES 
                    ExtraerPestana(driver, wait, "Precursores regulares", true, diccionarioExtraidos, false);

                    // 2. EXTRAER PRECURSORES ESPECIALES 
                    ExtraerPestana(driver, wait, "En el campo", false, diccionarioExtraidos, false);

                    // 3. EXTRAER ANCIANOS 
                    ExtraerPestana(driver, wait, "Ancianos", false, diccionarioExtraidos, false);

                    // 4. EXTRAER SIERVOS MINISTERIALES 
                    ExtraerPestana(driver, wait, "Siervos ministeriales", false, diccionarioExtraidos, false);

                    // 5. EXTRAER SOLICITUDES VIGENTES
                    ExtraerPestana(driver, wait, "Solicitudes vigentes", false, diccionarioExtraidos, true);

                    // 6. EXTRAER PRIVILEGIOS
                    ExtraerPrivilegios(driver, wait, diccionarioExtraidos);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error de navegación: " + ex.Message, "Error JW Hub", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                finally
                {
                    driver.Quit();
                }
            }
            return diccionarioExtraidos.Values.ToList();
        }

        private static void ExtraerPestana(IWebDriver driver, WebDriverWait wait, string textoEnlace, bool extraerDetalles, Dictionary<string, PersonaExtraida> diccionario, bool esSolicitudes = false)
        {
            bool entroEnPestana = false;
            try
            {
                Thread.Sleep(5000);
                var btn = wait.Until(d => d.FindElement(By.XPath($"//a[contains(@class, 'data__link') and contains(text(), '{textoEnlace}')]")));
                btn.Click();
                entroEnPestana = true;
                Thread.Sleep(5000);

                while (true)
                {
                    try
                    {
                        Thread.Sleep(5000);
                        var btnMostrarMas = driver.FindElements(By.XPath("//button[contains(@class, 'margin-top-05') and .//ptrn-translate[contains(text(), 'Mostrar') and contains(text(), 'más')]]")).FirstOrDefault(b => b.Displayed);
                        if (btnMostrarMas != null) btnMostrarMas.Click();
                        else break;
                    }
                    catch { break; }
                }

                try
                {
                    var btnMostrarTodo = driver.FindElements(By.XPath("//button[.//ptrn-translate[contains(text(), 'Mostrar todo')]]")).FirstOrDefault(b => b.Displayed);
                    if (btnMostrarTodo != null) { btnMostrarTodo.Click(); Thread.Sleep(5000); }
                }
                catch { }

                var cardElements = driver.FindElements(By.CssSelector("article"));

                string[] privilegiosPermitidos = {
                    "Instructor de la Escuela del Servicio de Precursor", "Superintendente de circuito sustituto",
                    "Miembro del CEH", "Superintendente de asamblea", "Coordinador", "Secretario",
                    "Miembro del GVP", "Persona que pertenece a una entidad del LDC", "Superintendente auxiliar de asamblea"
                };

                foreach (var card in cardElements)
                {
                    var spanNombre = card.FindElements(By.CssSelector("span.transliteration__vernacular")).FirstOrDefault();
                    if (spanNombre == null || string.IsNullOrWhiteSpace(spanNombre.Text)) continue;

                    string nombreReal = spanNombre.Text.Trim();

                    if (!diccionario.ContainsKey(nombreReal))
                    {
                        diccionario[nombreReal] = new PersonaExtraida { NombreCompleto = nombreReal };
                        string[] partesNombre = nombreReal.Split(',');
                        diccionario[nombreReal].Apellidos = partesNombre[0].Trim();
                    }

                    var persona = diccionario[nombreReal];

                    var badges = card.FindElements(By.CssSelector("ptrn-badges .badge"));
                    foreach (var badge in badges)
                    {
                        string textoBadge = badge.Text.Trim();

                        if (textoBadge.Contains("Misionero", StringComparison.OrdinalIgnoreCase)) persona.Servicio = "Misionero";
                        else if (textoBadge.Contains("Precursor especial", StringComparison.OrdinalIgnoreCase)) persona.Servicio = "Precursor especial";
                        else if (textoBadge.Contains("Precursor", StringComparison.OrdinalIgnoreCase)) { if (persona.Servicio != "Precursor especial") persona.Servicio = "Precursor regular"; }
                        else if (textoBadge.Contains("Anciano", StringComparison.OrdinalIgnoreCase)) persona.Nombramiento = "Anciano";
                        else if (textoBadge.Contains("Siervo", StringComparison.OrdinalIgnoreCase)) persona.Nombramiento = "Siervo ministerial";
                    }

                    // =========================================================
                    // EXTRACCIÓN DE FOTO (Nueva pestaña para tamaño completo)
                    // =========================================================
                    try
                    {
                        var imgElement = card.FindElements(By.CssSelector("ptrn-picture img")).FirstOrDefault();
                        if (imgElement != null)
                        {
                            string hiddenAttr = imgElement.GetAttribute("hidden");
                            string src = imgElement.GetAttribute("src");

                            // Si la persona tiene foto subida (no está oculta)
                            if (hiddenAttr == null && !string.IsNullOrEmpty(src))
                            {
                                string ventanaPrincipal = driver.CurrentWindowHandle;

                                // Abrimos la URL de la imagen en una nueva pestaña
                                ((IJavaScriptExecutor)driver).ExecuteScript("window.open(arguments[0], '_blank');", src);
                                Thread.Sleep(500); // Pequeña pausa para que el navegador cree la pestaña

                                // Saltamos a la nueva pestaña
                                driver.SwitchTo().Window(driver.WindowHandles.Last());

                                try
                                {
                                    Thread.Sleep(2000); // Esperamos a que la imagen cargue completamente

                                    // Usamos Canvas para extraer la imagen sin recortes
                                    string script = @"
                                        var img = document.querySelector('img');
                                        if (!img) return '';
                                        var canvas = document.createElement('canvas');
                                        canvas.width = img.naturalWidth;
                                        canvas.height = img.naturalHeight;
                                        var ctx = canvas.getContext('2d');
                                        ctx.drawImage(img, 0, 0);
                                        return canvas.toDataURL('image/jpeg', 0.8);
                                    ";

                                    string base64Foto = (string)((IJavaScriptExecutor)driver).ExecuteScript(script);

                                    // Respaldo de seguridad por si Canvas falla
                                    if (string.IsNullOrEmpty(base64Foto))
                                    {
                                        var imgSola = driver.FindElement(By.TagName("img"));
                                        Screenshot captura = ((ITakesScreenshot)imgSola).GetScreenshot();
                                        base64Foto = captura.AsBase64EncodedString;
                                    }

                                    if (!string.IsNullOrEmpty(base64Foto))
                                    {
                                        // Limpiamos la cabecera 'data:image...' si existe
                                        if (base64Foto.Contains(","))
                                            base64Foto = base64Foto.Split(',')[1];

                                        persona.Foto = base64Foto;
                                    }
                                }
                                finally
                                {
                                    // SIEMPRE cerramos la pestaña de la foto y volvemos a la principal
                                    driver.Close();
                                    driver.SwitchTo().Window(ventanaPrincipal);
                                }
                            }
                        }
                    }
                    catch { }
                    // =========================================================

                    var paresDatos = card.FindElements(By.CssSelector("dl div.data__pair"));

                    foreach (var par in paresDatos)
                    {
                        var dt = par.FindElements(By.TagName("dt")).FirstOrDefault();
                        if (dt == null) continue;

                        string etiqueta = dt.Text.ToLower();
                        var dds = par.FindElements(By.TagName("dd"));
                        if (dds.Count == 0) continue;

                        string valorUnido = string.Join(", ", dds.Select(d => d.Text.Trim()));

                        if (etiqueta.Contains("correo") || etiqueta.Contains("email"))
                            persona.Email = valorUnido;

                        else if (etiqueta.Contains("teléfono") || etiqueta.Contains("celular"))
                            persona.Telefono = valorUnido.Replace(" ", "").Replace("+34", "");

                        else if (etiqueta.Contains("congregación"))
                            persona.Congregacion = ExtraerCodigoCongregacion(valorUnido);

                        else if (etiqueta.Contains("solicitud"))
                        {
                            string sol = ExtraerCodigosSolicitudes(valorUnido);
                            if (!string.IsNullOrEmpty(sol)) persona.Solicitudes = sol;
                        }
                        else if (etiqueta.Contains("privilegio"))
                        {
                            var misPrivilegios = new List<string>();
                            foreach (var dd in dds)
                            {
                                string privTexto = dd.GetAttribute("textContent").Trim();
                                if (privilegiosPermitidos.Any(p => privTexto.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    misPrivilegios.Add(privTexto);
                                }
                            }
                            if (misPrivilegios.Count > 0) persona.Privilegios = string.Join(", ", misPrivilegios);
                        }
                        else if (extraerDetalles && (etiqueta.Contains("inicio") || etiqueta.Contains("comenzó")))
                            persona.FechaInicio = valorUnido;

                        else if (extraerDetalles && (etiqueta.Contains("escuela") || etiqueta.Contains("asistió")))
                            persona.FechaUltimaEscuela = valorUnido;
                    }
                }
            }
            catch { }
            finally
            {
                if (entroEnPestana) { try { driver.Navigate().Back(); Thread.Sleep(2000); } catch { } }
            }
        }

        private static string ExtraerCodigoCongregacion(string textoOriginal)
        {
            if (string.IsNullOrWhiteSpace(textoOriginal)) return "";

            int inicio = textoOriginal.LastIndexOf('(');
            int fin = textoOriginal.LastIndexOf(')');

            if (inicio >= 0 && fin > inicio)
            {
                return textoOriginal.Substring(inicio + 1, fin - inicio - 1).Trim();
            }
            return textoOriginal.Trim();
        }

        private static string ExtraerCodigosSolicitudes(string textoOriginal)
        {
            if (string.IsNullOrWhiteSpace(textoOriginal)) return "";

            var coincidencias = System.Text.RegularExpressions.Regex.Matches(textoOriginal, @"[A-Z]+-\d+");
            if (coincidencias.Count > 0)
            {
                var codigos = new List<string>();
                foreach (System.Text.RegularExpressions.Match match in coincidencias)
                {
                    codigos.Add(match.Value);
                }
                return string.Join(", ", codigos);
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(textoOriginal.Trim(), @"^\d+$"))
                return "";

            return textoOriginal.Trim();
        }

        private static void ExtraerPrivilegios(IWebDriver driver, WebDriverWait wait, Dictionary<string, PersonaExtraida> diccionario)
        {
            bool entroEnPestana = false;
            try
            {
                Thread.Sleep(5000);
                var btn = wait.Until(d => d.FindElement(By.XPath("//a[contains(@class, 'data__link') or contains(@class, 'side-nav__link')][contains(text(), 'Registros de personas')]")));
                btn.Click();
                entroEnPestana = true;
                Thread.Sleep(5000);

                // =========================================================
                // LIMPIAR EL BUSCADOR (Pulsar la "X")
                // =========================================================
                try
                {
                    var btnLimpiar = driver.FindElements(By.CssSelector("ptrn-icon[icon='close']")).FirstOrDefault();
                    if (btnLimpiar != null && btnLimpiar.Displayed)
                    {
                        btnLimpiar.Click();
                        Thread.Sleep(1500);
                    }
                }
                catch { }
                // =========================================================

                var searchInput = driver.FindElements(By.CssSelector("input[type='search']")).FirstOrDefault();
                if (searchInput != null)
                {
                    searchInput.Click();
                    Thread.Sleep(500);
                    searchInput.SendKeys(" ");
                    Thread.Sleep(500);
                    searchInput.SendKeys(Keys.Backspace);
                    Thread.Sleep(500);
                    searchInput.SendKeys(Keys.Enter);
                    Thread.Sleep(5000);
                }

                while (true)
                {
                    try
                    {
                        Thread.Sleep(4000);
                        var btnMostrarMas = driver.FindElements(By.XPath("//button[contains(@class, 'margin-top-05') and .//ptrn-translate[contains(text(), 'Mostrar') and contains(text(), 'más')]]")).FirstOrDefault(b => b.Displayed);
                        if (btnMostrarMas != null) btnMostrarMas.Click();
                        else break;
                    }
                    catch { break; }
                }

                try
                {
                    var btnMostrarTodo = driver.FindElements(By.XPath("//button[.//ptrn-translate[contains(text(), 'Mostrar todo')]]")).FirstOrDefault(b => b.Displayed);
                    if (btnMostrarTodo != null) { btnMostrarTodo.Click(); Thread.Sleep(5000); }
                }
                catch { }

                var cardElements = driver.FindElements(By.CssSelector("article"));

                string[] privilegiosPermitidos = {
                    "Instructor de la Escuela del Servicio de Precursor", "Superintendente de circuito sustituto",
                    "Miembro del CEH", "Superintendente de asamblea", "Coordinador", "Secretario",
                    "Miembro del GVP", "Persona que pertenece a una entidad del LDC", "Superintendente auxiliar de asamblea"
                };

                foreach (var card in cardElements)
                {
                    var spanNombre = card.FindElements(By.CssSelector("span.transliteration__vernacular")).FirstOrDefault();
                    if (spanNombre == null || string.IsNullOrWhiteSpace(spanNombre.Text)) continue;

                    string nombreReal = spanNombre.Text.Trim();

                    if (!diccionario.ContainsKey(nombreReal))
                    {
                        diccionario[nombreReal] = new PersonaExtraida { NombreCompleto = nombreReal };
                        diccionario[nombreReal].Apellidos = nombreReal.Split(',')[0].Trim();
                    }

                    var persona = diccionario[nombreReal];

                    // =========================================================
                    // EXTRACCIÓN DE FOTO (Nueva pestaña para tamaño completo)
                    // =========================================================
                    try
                    {
                        var imgElement = card.FindElements(By.CssSelector("ptrn-picture img")).FirstOrDefault();
                        if (imgElement != null)
                        {
                            string hiddenAttr = imgElement.GetAttribute("hidden");
                            string src = imgElement.GetAttribute("src");

                            if (hiddenAttr == null && !string.IsNullOrEmpty(src))
                            {
                                string ventanaPrincipal = driver.CurrentWindowHandle;

                                ((IJavaScriptExecutor)driver).ExecuteScript("window.open(arguments[0], '_blank');", src);
                                Thread.Sleep(500);

                                driver.SwitchTo().Window(driver.WindowHandles.Last());

                                try
                                {
                                    Thread.Sleep(2000);

                                    string script = @"
                                        var img = document.querySelector('img');
                                        if (!img) return '';
                                        var canvas = document.createElement('canvas');
                                        canvas.width = img.naturalWidth;
                                        canvas.height = img.naturalHeight;
                                        var ctx = canvas.getContext('2d');
                                        ctx.drawImage(img, 0, 0);
                                        return canvas.toDataURL('image/jpeg', 0.8);
                                    ";

                                    string base64Foto = (string)((IJavaScriptExecutor)driver).ExecuteScript(script);

                                    if (string.IsNullOrEmpty(base64Foto))
                                    {
                                        var imgSola = driver.FindElement(By.TagName("img"));
                                        Screenshot captura = ((ITakesScreenshot)imgSola).GetScreenshot();
                                        base64Foto = captura.AsBase64EncodedString;
                                    }

                                    if (!string.IsNullOrEmpty(base64Foto))
                                    {
                                        if (base64Foto.Contains(","))
                                            base64Foto = base64Foto.Split(',')[1];

                                        persona.Foto = base64Foto;
                                    }
                                }
                                finally
                                {
                                    driver.Close();
                                    driver.SwitchTo().Window(ventanaPrincipal);
                                }
                            }
                        }
                    }
                    catch { }
                    // =========================================================

                    var paresDatos = card.FindElements(By.CssSelector("dl div.data__pair"));

                    foreach (var par in paresDatos)
                    {
                        var dt = par.FindElements(By.TagName("dt")).FirstOrDefault();
                        if (dt == null) continue;

                        string etiqueta = dt.Text.ToLower();

                        if (etiqueta.Contains("privilegio"))
                        {
                            var dds = par.FindElements(By.TagName("dd"));
                            var misPrivilegios = new List<string>();

                            foreach (var dd in dds)
                            {
                                string privTexto = dd.GetAttribute("textContent").Trim();
                                if (privilegiosPermitidos.Any(p => privTexto.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    misPrivilegios.Add(privTexto);
                                }
                            }
                            if (misPrivilegios.Count > 0) persona.Privilegios = string.Join(", ", misPrivilegios);
                        }
                        else if (etiqueta.Contains("congregación"))
                        {
                            var dds = par.FindElements(By.TagName("dd"));
                            if (dds.Count > 0)
                            {
                                string valorUnido = string.Join(", ", dds.Select(d => d.GetAttribute("textContent").Trim()));
                                persona.Congregacion = ExtraerCodigoCongregacion(valorUnido);
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (entroEnPestana) { try { driver.Navigate().Back(); Thread.Sleep(2000); } catch { } }
            }
        }
    }

    public class PersonaExtraida
    {
        public string NombreCompleto { get; set; }
        public string Apellidos { get; set; }
        public string FechaInicio { get; set; }
        public string FechaUltimaEscuela { get; set; }
        public string Email { get; set; }
        public string Telefono { get; set; }
        public string Congregacion { get; set; }
        public string Servicio { get; set; }
        public string Nombramiento { get; set; }
        public string Solicitudes { get; set; }
        public string Privilegios { get; set; }
        public string Foto { get; set; }
    }
}