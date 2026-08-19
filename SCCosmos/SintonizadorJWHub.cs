using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace SCCosmos
{
    public static class SincronizadorJWHub
    {
        public static List<PersonaExtraida> SincronizarGlobal(string rutaPerfilFirefox)
        {
            var listaExtraidos = new List<PersonaExtraida>();
            string urlBase = "https://hub.jw.org";

            var service = FirefoxDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true; // Oculta la consola negra de Geckodriver

            FirefoxOptions options = new FirefoxOptions();
            options.AddArgument("-profile");
            options.AddArgument(rutaPerfilFirefox);

            // === MODO HEADLESS DESACTIVADO PARA PRUEBAS ===
            // options.AddArgument("--headless"); 

            // CONFIGURACIÓN PARA DESCARGAS DE NOMBRAMIENTOS
            string tempDownloadPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempDownloads");
            if (!Directory.Exists(tempDownloadPath)) Directory.CreateDirectory(tempDownloadPath);

            options.SetPreference("browser.download.folderList", 2);
            options.SetPreference("browser.download.dir", tempDownloadPath);
            options.SetPreference("browser.helperApps.neverAsk.saveToDisk", "application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/zip,image/jpeg,image/png,application/octet-stream");
            options.SetPreference("pdfjs.disabled", true);

            using (IWebDriver driver = new FirefoxDriver(service, options))
            {
                try
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);

                    // =========================================================================
                    // FASE 1: EXTRAER PERSONAS
                    // =========================================================================

                    driver.Navigate().GoToUrl(urlBase + "/");

                    // 1. Ir a Personas de la congregación
                    var btnPersonas = EsperaDinamicaElementoClicable(driver, By.XPath("//a[contains(@href, 'congregation-persons') or contains(text(), 'Personas de la congregación')]"), 60);

                    if (btnPersonas != null) btnPersonas.Click();
                    else
                    {
                        System.Windows.MessageBox.Show("No se pudo acceder a la sección de Personas. ¿La sesión de JW Hub ha caducado o la página no cargó?", "Aviso", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return listaExtraidos;
                    }

                    // 2. Ir a "Registros de personas"
                    var btnRegistros = EsperaDinamicaElementoClicable(driver, By.XPath("//a[contains(@href, '/records') or contains(text(), 'Registros de personas')]"), 15);
                    if (btnRegistros != null) btnRegistros.Click();
                    else return listaExtraidos;

                    Thread.Sleep(2000); // Dar tiempo a que cargue la vista de registros

                    // ---> MODO DE PRUEBA: Buscar solo la palabra "badenas"
                    /*try
                    {
                        var inputBuscar = driver.FindElements(By.XPath("//input[@type='search' or contains(@placeholder, 'Introduzca un nombre')]")).FirstOrDefault(i => i.Displayed && i.Enabled);

                        if (inputBuscar != null)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", inputBuscar);
                            Thread.Sleep(500);
                            inputBuscar.Clear();
                            inputBuscar.SendKeys("badenas");
                            Thread.Sleep(1000);
                        }
                    }
                    catch { }*/
                    // <--- FIN MODO DE PRUEBA

                    // 3. Pulsar el botón "Buscar"
                    var btnBuscar = EsperaDinamicaElementoClicable(driver, By.XPath("//button[.//span[contains(text(), 'Buscar')]]"), 15);
                    if (btnBuscar != null)
                    {
                        Thread.Sleep(1000);
                        btnBuscar.Click();
                    }

                    EsperaDinamicaElementoVisibilidad(driver, By.CssSelector("article"), 15);

                    // 4. Mostrar más
                    while (true)
                    {
                        var btnMostrarMas = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//button[contains(@class, 'margin-top-05') and .//ptrn-translate[contains(text(), 'Mostrar') and contains(text(), 'más')]]"), 3);
                        if (btnMostrarMas != null && btnMostrarMas.Displayed)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnMostrarMas);
                            Thread.Sleep(200);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnMostrarMas);
                        }
                        else break;
                    }

                    // 5. Mostrar todo
                    var btnMostrarTodo = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//button[.//ptrn-translate[contains(text(), 'Mostrar todo')]]"), 3);
                    if (btnMostrarTodo != null && btnMostrarTodo.Displayed)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnMostrarTodo);
                        Thread.Sleep(200);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnMostrarTodo);
                        EsperaDinamicaElementoVisibilidad(driver, By.CssSelector("article"), 15);
                    }

                    // 6. Recorrer la lista
                    int totalCards = driver.FindElements(By.CssSelector("article")).Count;
                    string[] privilegiosPermitidos = {
                        "Instructor de la Escuela del Servicio de Precursor", "Superintendente de circuito sustituto",
                        "Miembro del CEH", "Superintendente de asamblea", "Coordinador", "Secretario",
                        "Miembro del GVP", "Persona que pertenece a una entidad del LDC", "Superintendente auxiliar de asamblea"
                    };

                    for (int i = 0; i < totalCards; i++)
                    {
                        var cards = driver.FindElements(By.CssSelector("article"));
                        while (i >= cards.Count)
                        {
                            try
                            {
                                var btnMas = driver.FindElements(By.XPath("//button[contains(@class, 'margin-top-05') and .//ptrn-translate[contains(text(), 'Mostrar') and contains(text(), 'más')]]")).FirstOrDefault(b => b.Displayed);
                                if (btnMas != null)
                                {
                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnMas);
                                    Thread.Sleep(200);
                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnMas);
                                    EsperaDinamicaElementoVisibilidad(driver, By.CssSelector("article"), 5);
                                    cards = driver.FindElements(By.CssSelector("article"));
                                }
                                else
                                {
                                    var btnTodo = driver.FindElements(By.XPath("//button[.//ptrn-translate[contains(text(), 'Mostrar todo')]]")).FirstOrDefault(b => b.Displayed);
                                    if (btnTodo != null)
                                    {
                                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnTodo);
                                        Thread.Sleep(200);
                                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnTodo);
                                        EsperaDinamicaElementoVisibilidad(driver, By.CssSelector("article"), 5);
                                        cards = driver.FindElements(By.CssSelector("article"));
                                    }
                                    else break;
                                }
                            }
                            catch { break; }
                        }

                        if (i >= cards.Count) continue;
                        var card = cards[i];

                        var spanNombre = card.FindElements(By.CssSelector("span.transliteration__vernacular")).FirstOrDefault();
                        if (spanNombre == null || string.IsNullOrWhiteSpace(spanNombre.Text)) continue;

                        string nombreReal = spanNombre.Text.Replace("\u200B", "").Trim();
                        var tempPersona = new PersonaExtraida { NombreCompleto = nombreReal };
                        string[] partesNombre = nombreReal.Split(',');

                        // Invertimos "Apellidos, Nombre" a "Nombre Apellidos"
                        if (partesNombre.Length >= 2)
                        {
                            string apellidos = partesNombre[0].Trim();
                            string nombrePila = partesNombre[1].Trim();
                            tempPersona.NombreComun = $"{nombrePila} {apellidos}";
                            tempPersona.Apellidos = apellidos;
                        }
                        else
                        {
                            tempPersona.NombreComun = nombreReal.Trim();
                            tempPersona.Apellidos = "";
                        }

                        // Badges (Nombramiento / Servicio)
                        var badges = card.FindElements(By.CssSelector("ptrn-badges .badge"));
                        foreach (var badge in badges)
                        {
                            // Usamos textContent puro para evadir Angular
                            string textoBadge = badge.GetAttribute("textContent").Trim();
                            if (textoBadge.Contains("Misionero", StringComparison.OrdinalIgnoreCase)) tempPersona.Servicio = "Misionero";
                            else if (textoBadge.Contains("Precursor especial", StringComparison.OrdinalIgnoreCase)) tempPersona.Servicio = "Precursor especial";
                            else if (textoBadge.Contains("Precursor", StringComparison.OrdinalIgnoreCase)) { if (tempPersona.Servicio != "Precursor especial") tempPersona.Servicio = "Precursor regular"; }
                            else if (textoBadge.Contains("Anciano", StringComparison.OrdinalIgnoreCase)) tempPersona.Nombramiento = "Anciano";
                            else if (textoBadge.Contains("Siervo", StringComparison.OrdinalIgnoreCase)) tempPersona.Nombramiento = "Siervo ministerial";
                        }

                        // Datos Exteriores
                        var paresDatos = card.FindElements(By.CssSelector("dl div.data__pair"));
                        foreach (var par in paresDatos)
                        {
                            var dt = par.FindElements(By.TagName("dt")).FirstOrDefault();
                            if (dt == null) continue;

                            string etiqueta = dt.GetAttribute("textContent").ToLower();

                            if (etiqueta.Contains("privilegio"))
                            {
                                var dds = par.FindElements(By.TagName("dd"));
                                var misPrivilegios = new List<string>();
                                foreach (var dd in dds)
                                {
                                    string privTexto = dd.GetAttribute("textContent").Trim();
                                    if (privilegiosPermitidos.Any(p => privTexto.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) misPrivilegios.Add(privTexto);
                                }
                                if (misPrivilegios.Count > 0) tempPersona.Privilegios = string.Join("\n", misPrivilegios);
                            }
                            else if (etiqueta.Contains("congregación") || etiqueta.Contains("congregacion"))
                            {
                                var dds = par.FindElements(By.TagName("dd"));
                                if (dds.Count > 0) { string valorUnido = string.Join(", ", dds.Select(d => d.GetAttribute("textContent").Trim())); tempPersona.Congregacion = ExtraerCodigoCongregacion(valorUnido); }
                            }
                            else if (etiqueta.Contains("edad") || etiqueta.Contains("nacimiento"))
                            {
                                var dds = par.FindElements(By.TagName("dd"));
                                if (dds.Count > 0)
                                {
                                    string valorTxt = dds.First().GetAttribute("textContent").Trim();
                                    var matchYear = System.Text.RegularExpressions.Regex.Match(valorTxt, @"\b(19|20)\d{2}\b");
                                    if (matchYear.Success)
                                    {
                                        tempPersona.FechaNacimiento = ConvertirFechaJWHub(valorTxt);
                                    }
                                    else
                                    {
                                        // Es una edad tipo "26,6"
                                        string edadStr = valorTxt.Split(new char[] { ',', '.' })[0].Trim();
                                        string digitos = new string(edadStr.Where(char.IsDigit).ToArray());
                                        if (!string.IsNullOrEmpty(digitos) && int.TryParse(digitos, out int edad))
                                            tempPersona.FechaNacimiento = $"01/01/{DateTime.Now.Year - edad}";
                                    }
                                }
                            }
                            else if (etiqueta.Contains("bautismo") || etiqueta.Contains("bautizado"))
                            {
                                var dds = par.FindElements(By.TagName("dd"));
                                if (dds.Count > 0)
                                {
                                    string valorTxt = dds.First().GetAttribute("textContent").Trim();
                                    var matchYear = System.Text.RegularExpressions.Regex.Match(valorTxt, @"\b(19|20)\d{2}\b");
                                    if (matchYear.Success)
                                    {
                                        tempPersona.FechaBautismo = ConvertirFechaJWHub(valorTxt);
                                    }
                                    else
                                    {
                                        // Son años tipo "12,1"
                                        string edadStr = valorTxt.Split(new char[] { ',', '.' })[0].Trim();
                                        string digitos = new string(edadStr.Where(char.IsDigit).ToArray());
                                        if (!string.IsNullOrEmpty(digitos) && int.TryParse(digitos, out int aniosBaut))
                                            tempPersona.FechaBautismo = $"01/01/{DateTime.Now.Year - aniosBaut}";
                                    }
                                }
                            }
                            else
                            {
                                var dds = par.FindElements(By.TagName("dd"));
                                if (dds.Count == 0) continue;
                                string valorUnido = string.Join(", ", dds.Select(d => d.GetAttribute("textContent").Trim()));

                                if (etiqueta.Contains("teléfono") || etiqueta.Contains("celular") || etiqueta.Contains("movil") || etiqueta.Contains("móvil") || etiqueta.Contains("telefono"))
                                {
                                    string tlfRaw = valorUnido.Replace(" ", "").Replace("+34", "").Replace("-", "").Trim();
                                    if ((tlfRaw.StartsWith("6") || tlfRaw.StartsWith("7")) && string.IsNullOrEmpty(tempPersona.Telefono))
                                    {
                                        tempPersona.Telefono = tlfRaw;
                                    }
                                }
                            }
                        }

                        // --- EXTRACCIÓN PROFUNDA EN PESTAÑA NUEVA ---
                        try
                        {
                            var btnVerMas = card.FindElements(By.XPath(".//a[contains(@class, 'button--action') and .//ptrn-translate[contains(text(), 'Ver más')]]")).FirstOrDefault()
                                         ?? card.FindElements(By.XPath(".//a[contains(@class, 'button') and .//ptrn-translate[contains(text(), 'Ver más')]]")).FirstOrDefault()
                                         ?? card.FindElements(By.XPath(".//a[contains(@href, 'person') and .//ptrn-translate[contains(text(), 'Ver más')]]")).FirstOrDefault();

                            if (btnVerMas != null)
                            {
                                string urlPerfil = btnVerMas.GetAttribute("href");

                                if (!string.IsNullOrEmpty(urlPerfil))
                                {
                                    string ventanaListado = driver.CurrentWindowHandle;

                                    ((IJavaScriptExecutor)driver).ExecuteScript("window.open(arguments[0], '_blank');", urlPerfil);
                                    Thread.Sleep(500);
                                    driver.SwitchTo().Window(driver.WindowHandles.Last());

                                    try
                                    {
                                        EsperaDinamicaElementoVisibilidad(driver, By.TagName("app-address"), 15);

                                        // --- 1. FOTO ---
                                        try
                                        {
                                            var imgElement = driver.FindElements(By.CssSelector("div.bg-g300 img[alt='Fotografía principal']")).FirstOrDefault()
                                                          ?? driver.FindElements(By.CssSelector("img[alt='Fotografía principal']")).FirstOrDefault();

                                            if (imgElement != null)
                                            {
                                                string src = imgElement.GetAttribute("src");
                                                if (!string.IsNullOrEmpty(src))
                                                {
                                                    string ventanaPerfil = driver.CurrentWindowHandle;
                                                    int handleCountBefore = driver.WindowHandles.Count;

                                                    ((IJavaScriptExecutor)driver).ExecuteScript("window.open(arguments[0], '_blank');", src);
                                                    Thread.Sleep(1000);

                                                    if (driver.WindowHandles.Count > handleCountBefore)
                                                    {
                                                        driver.SwitchTo().Window(driver.WindowHandles.Last());

                                                        try
                                                        {
                                                            WebDriverWait w = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                                                            w.Until(d => (bool)((IJavaScriptExecutor)d).ExecuteScript("return document.readyState == 'complete'"));
                                                            Thread.Sleep(500);

                                                            string script = "var img = document.querySelector('img'); if (!img) return ''; var canvas = document.createElement('canvas'); canvas.width = img.naturalWidth; canvas.height = img.naturalHeight; var ctx = canvas.getContext('2d'); ctx.drawImage(img, 0, 0); return canvas.toDataURL('image/jpeg', 0.8);";
                                                            string base64Foto = (string)((IJavaScriptExecutor)driver).ExecuteScript(script);

                                                            if (string.IsNullOrEmpty(base64Foto))
                                                            {
                                                                var imgSola = driver.FindElement(By.TagName("img"));
                                                                Screenshot captura = ((ITakesScreenshot)imgSola).GetScreenshot();
                                                                base64Foto = captura.AsBase64EncodedString;
                                                            }

                                                            if (!string.IsNullOrEmpty(base64Foto))
                                                            {
                                                                if (base64Foto.Contains(",")) base64Foto = base64Foto.Split(',')[1];
                                                                tempPersona.Foto = base64Foto;
                                                            }
                                                        }
                                                        catch { }
                                                        finally
                                                        {
                                                            driver.Close();
                                                            driver.SwitchTo().Window(ventanaPerfil);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch { }

                                        // --- 2. EMAILS ---
                                        try
                                        {
                                            var emails = driver.FindElements(By.CssSelector("bdi.email"));
                                            foreach (var email in emails)
                                            {
                                                string textoEmail = email.GetAttribute("textContent").Trim();
                                                if (textoEmail.EndsWith("@jwpub.org", StringComparison.OrdinalIgnoreCase)) tempPersona.EmailJW = textoEmail;
                                                else if (!string.IsNullOrEmpty(textoEmail)) tempPersona.Email = textoEmail;
                                            }
                                        }
                                        catch { }

                                        // --- 3. DIRECCIÓN FÍSICA ---
                                        try
                                        {
                                            // Usamos . en lugar de text() para evadir spans
                                            var appAddress = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'dirección física') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'dirección postal')]/following-sibling::dd//app-address")).FirstOrDefault();
                                            if (appAddress != null)
                                            {
                                                string dirRaw = appAddress.GetAttribute("textContent").Trim();
                                                tempPersona.Direccion = string.Join(", ", dirRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
                                            }
                                        }
                                        catch { }

                                        // --- RECOLECCIÓN DEL OVERVIEW ---
                                        try
                                        {
                                            // ESPERANZA
                                            var ddEsperanza = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'esperanza')]/following-sibling::dd")).FirstOrDefault();
                                            if (ddEsperanza != null)
                                            {
                                                string txtEsp = ddEsperanza.GetAttribute("textContent").Trim();
                                                if (txtEsp.Contains("Otras", StringComparison.OrdinalIgnoreCase)) tempPersona.Esperanza = "Otras ovejas";
                                                else if (txtEsp.Contains("Ungido", StringComparison.OrdinalIgnoreCase)) tempPersona.Esperanza = "Ungido";
                                            }

                                            // INICIO PRECURSORADO
                                            var ddInicio = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'inicio del precursorado')]/following-sibling::dd")).FirstOrDefault();
                                            if (ddInicio != null)
                                            {
                                                tempPersona.FechaInicioPrec = ConvertirFechaJWHub(ddInicio.GetAttribute("textContent").Trim());
                                            }
                                        }
                                        catch { }

                                        // --- 4. SOLICITUDES VIGENTES ---
                                        var btnSolicitudes = EsperaDinamicaElementoClicable(driver, By.XPath("//a[contains(@href, '/applications') or contains(., 'Solicitudes vigentes')]"), 3);
                                        if (btnSolicitudes != null)
                                        {
                                            btnSolicitudes.Click();
                                            Thread.Sleep(2000);
                                            try
                                            {
                                                string htmlSols = driver.PageSource;
                                                var matches = System.Text.RegularExpressions.Regex.Matches(htmlSols, @"\b(A-2|A-8|G-8|A-19)\b");
                                                var solList = new List<string>();

                                                foreach (System.Text.RegularExpressions.Match m in matches)
                                                {
                                                    solList.Add(m.Value);
                                                }

                                                if (solList.Count > 0) tempPersona.Solicitudes = string.Join(", ", solList.Distinct());
                                            }
                                            catch { }
                                        }

                                        // --- 5. EDUCACIÓN TEOCRÁTICA ---
                                        var btnEducacion = EsperaDinamicaElementoClicable(driver, By.XPath("//a[contains(@href, '/theocratic-education') or contains(., 'Educación teocrática')]"), 3);
                                        if (btnEducacion != null)
                                        {
                                            btnEducacion.Click();
                                            Thread.Sleep(2000);
                                            try
                                            {
                                                var headerEscuela = driver.FindElements(By.XPath("//h2[contains(., 'Pioneer Service School') or contains(., 'Escuela del Servicio de Precursor') or contains(., 'Escuela para Evangelizadores')]")).FirstOrDefault();
                                                if (headerEscuela != null)
                                                {
                                                    var cardEscuela = headerEscuela.FindElement(By.XPath("./ancestor::article[contains(@class, 'card')]"));
                                                    var ddGrad = cardEscuela.FindElements(By.XPath(".//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'fecha de graduación')]/following-sibling::dd")).FirstOrDefault();

                                                    if (ddGrad != null)
                                                    {
                                                        tempPersona.FechaUltimaEscuela = ConvertirFechaJWHub(ddGrad.GetAttribute("textContent"));
                                                    }
                                                }
                                            }
                                            catch { }
                                        }

                                        // --- 6. ESTADO (Esperanza e Inicio Precursorado POR SI ACASO) ---
                                        var btnEstado = EsperaDinamicaElementoClicable(driver, By.XPath("//a[contains(@href, '/spiritual-status') or contains(., 'Estado')]"), 3);
                                        if (btnEstado != null)
                                        {
                                            btnEstado.Click();
                                            Thread.Sleep(2000);
                                            try
                                            {
                                                var ddEsperanza = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'esperanza')]/following-sibling::dd")).FirstOrDefault();
                                                if (ddEsperanza != null && string.IsNullOrEmpty(tempPersona.Esperanza))
                                                {
                                                    string textoEsperanza = ddEsperanza.GetAttribute("textContent").Trim();
                                                    if (textoEsperanza.Contains("Otras", StringComparison.OrdinalIgnoreCase)) tempPersona.Esperanza = "Otras ovejas";
                                                    else if (textoEsperanza.Contains("Ungido", StringComparison.OrdinalIgnoreCase)) tempPersona.Esperanza = "Ungido";
                                                }

                                                var ddInicio = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'inicio del precursorado')]/following-sibling::dd")).FirstOrDefault();
                                                if (ddInicio != null && string.IsNullOrEmpty(tempPersona.FechaInicioPrec))
                                                {
                                                    tempPersona.FechaInicioPrec = ConvertirFechaJWHub(ddInicio.GetAttribute("textContent").Trim());
                                                }
                                            }
                                            catch { }
                                        }

                                    }
                                    finally
                                    {
                                        driver.Close();
                                        driver.SwitchTo().Window(ventanaListado);
                                        Thread.Sleep(2000);
                                    }
                                }
                            }
                        }
                        catch { }

                        listaExtraidos.Add(tempPersona);
                    }

                    // =========================================================================
                    // FASE 2: EXTRAER NOMBRAMIENTOS Y DESCARGAR ARCHIVOS
                    // =========================================================================
                    string urlNombramientos = urlBase + "/congregation-persons/es/circuits/512bc4a5-94aa-ea11-815e-0050560012f5/appointments";
                    driver.Navigate().GoToUrl(urlNombramientos);
                    Thread.Sleep(5000);

                    string htmlNombramientos = driver.PageSource;
                    var recomendaciones = ExtractorNombramientosHelper.ExtraerRecomendaciones(htmlNombramientos);

                    if (recomendaciones.Count > 0)
                    {
                        DatabaseHelper.GuardarRecomendaciones(recomendaciones);
                    }

                    foreach (var rec in recomendaciones)
                    {
                        driver.Navigate().GoToUrl(urlBase + rec.UrlDetalle);
                        Thread.Sleep(4000);

                        var dlRec = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'recomendación')]/following-sibling::dd")).FirstOrDefault();
                        if (dlRec != null)
                        {
                            rec.RecomendadoComo = dlRec.GetAttribute("textContent").Trim();
                            DatabaseHelper.ActualizarRecomendadoComo(rec.Hermano, rec.RecomendadoComo);
                        }

                        var btnAnalizar = driver.FindElements(By.XPath("//a[.//ptrn-translate[contains(., 'Analizar hermano')]]")).FirstOrDefault();
                        if (btnAnalizar == null)
                        {
                            btnAnalizar = driver.FindElements(By.XPath("//*[contains(@class, 'button')]//ptrn-translate[contains(., 'Analizar hermano')]/ancestor::a")).FirstOrDefault();
                        }

                        if (btnAnalizar != null)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnAnalizar);
                            Thread.Sleep(500);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnAnalizar);

                            Thread.Sleep(3000);

                            LimpiarCarpetaTemporal(tempDownloadPath);

                            var spansDescargar = driver.FindElements(By.XPath("//span[contains(@class, 'button__text') and contains(., 'Descargar')]"));
                            foreach (var span in spansDescargar)
                            {
                                try
                                {
                                    var btnDescPadre = span.FindElement(By.XPath("./ancestor::*[contains(@class, 'button')][1] | ./.."));

                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnDescPadre);
                                    Thread.Sleep(500);
                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnDescPadre);

                                    Thread.Sleep(2500);
                                }
                                catch { }
                            }

                            Thread.Sleep(3000);
                            ProcesarArchivosDescargados(tempDownloadPath, rec.Hermano, "DescargaHub_" + rec.Hermano);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error durante la extracción: " + ex.Message, "Error JW Hub", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }

            return listaExtraidos;
        }

        public static void CancelarRecomendacionHub(string rutaPerfilFirefox, RecomendacionHub rec)
        {
            string urlBase = "https://hub.jw.org";
            var service = FirefoxDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;

            FirefoxOptions options = new FirefoxOptions();
            options.AddArgument("-profile");
            options.AddArgument(rutaPerfilFirefox);

            using (IWebDriver driver = new FirefoxDriver(service, options))
            {
                try
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);

                    driver.Navigate().GoToUrl(urlBase + rec.UrlDetalle);
                    Thread.Sleep(5000);

                    var btnAnalizar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//a[.//ptrn-translate[contains(., 'Analizar hermano')]] | //*[contains(@class, 'button')]//ptrn-translate[contains(., 'Analizar hermano')]/ancestor::a"), 10);
                    if (btnAnalizar != null)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnAnalizar);
                        Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnAnalizar);
                        Thread.Sleep(4000);

                        var btnEditar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//a[.//ptrn-translate[contains(., 'Editar estado del nombramiento')]]"), 10);
                        if (btnEditar != null)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnEditar);
                            Thread.Sleep(500);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEditar);
                            Thread.Sleep(3000);

                            // Marcar Cancelar recomendación
                            var lblCancelar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//label[.//ptrn-translate[contains(., 'Cancelar recomendación')]]"), 5);
                            if (lblCancelar != null)
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", lblCancelar);
                                Thread.Sleep(1000);
                            }

                            // Pulsar Guardar (Sin meter fecha)
                            var btnGuardar = driver.FindElements(By.XPath("//button[.//ptrn-translate[contains(., 'Guardar')]]")).FirstOrDefault();
                            if (btnGuardar != null)
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnGuardar);
                                Thread.Sleep(5000);
                            }
                        }
                    }

                    // Después de guardar, Enviar
                    var btnEnviar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//*[.//ptrn-translate[contains(., 'Enviar')]]/ancestor-or-self::button | //*[.//ptrn-translate[contains(., 'Enviar')]]/ancestor-or-self::a"), 10);
                    if (btnEnviar != null)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnEnviar);
                        Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);
                        Thread.Sleep(3000);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message);
                }
            }
        }

        public static void AprobarRecomendacionYEnviarCorreo(string rutaPerfilFirefox, RecomendacionHub rec, DateTime fechaAprobacion, List<string> rutasPdfs)
        {
            string urlBase = "https://hub.jw.org";

            var service = FirefoxDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;

            FirefoxOptions options = new FirefoxOptions();
            options.AddArgument("-profile");
            options.AddArgument(rutaPerfilFirefox);

            using (IWebDriver driver = new FirefoxDriver(service, options))
            {
                try
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);

                    driver.Navigate().GoToUrl(urlBase + rec.UrlDetalle);
                    Thread.Sleep(5000);

                    var btnAnalizar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//a[.//ptrn-translate[contains(., 'Analizar hermano')]] | //*[contains(@class, 'button')]//ptrn-translate[contains(., 'Analizar hermano')]/ancestor::a"), 10);
                    if (btnAnalizar != null)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnAnalizar);
                        Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnAnalizar);
                        Thread.Sleep(4000);

                        var btnEditar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//a[.//ptrn-translate[contains(., 'Editar estado del nombramiento')]]"), 10);
                        if (btnEditar != null)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnEditar);
                            Thread.Sleep(500);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEditar);
                            Thread.Sleep(3000);

                            var lblAprobar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//label[.//ptrn-translate[contains(., 'Aprobar')]]"), 5);
                            if (lblAprobar != null)
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", lblAprobar);
                                Thread.Sleep(1000);
                            }

                            var txtFecha = driver.FindElements(By.Id("form.effectiveDate")).FirstOrDefault() ??
                                           driver.FindElements(By.XPath("//input[@placeholder='d/M/yy']")).FirstOrDefault();

                            if (txtFecha != null)
                            {
                                txtFecha.Clear();
                                txtFecha.SendKeys(fechaAprobacion.ToString("d/M/yy"));
                                Thread.Sleep(500);
                                txtFecha.SendKeys(Keys.Tab);
                                Thread.Sleep(1000);
                            }

                            var btnGuardar = driver.FindElements(By.XPath("//button[.//ptrn-translate[contains(., 'Guardar')]]")).FirstOrDefault();
                            if (btnGuardar != null)
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnGuardar);
                                Thread.Sleep(5000);
                            }
                        }
                    }

                    var btnEnviar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//*[.//ptrn-translate[contains(., 'Enviar')]]/ancestor-or-self::button | //*[.//ptrn-translate[contains(., 'Enviar')]]/ancestor-or-self::a"), 10);
                    if (btnEnviar != null)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnEnviar);
                        Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);
                        Thread.Sleep(3000);
                    }


                    driver.Navigate().GoToUrl("https://mail.jwpub.org/owa/#path=/mail");
                    Thread.Sleep(8000);

                    var btnNuevo = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//button[contains(@aria-label, 'Nuevo') or .//span[contains(., 'Nuevo')]]"), 15);
                    if (btnNuevo != null)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnNuevo);
                        Thread.Sleep(3000);

                        var txtPara = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//input[contains(@aria-label, 'Para') or contains(@aria-label, 'To')]"), 5);
                        if (txtPara != null)
                        {
                            txtPara.SendKeys(rec.Congregacion);
                            Thread.Sleep(2000);
                            txtPara.SendKeys(Keys.Enter);
                        }

                        var txtAsunto = driver.FindElements(By.XPath("//input[contains(@aria-label, 'Asunto') or contains(@placeholder, 'Asunto') or contains(@aria-label, 'Subject')]")).FirstOrDefault();
                        if (txtAsunto != null)
                        {
                            string asunto = DatabaseHelper.ObtenerConfiguracion("Asunto_Nombramientos", "Cartas de Nombramiento");
                            txtAsunto.SendKeys(asunto);
                        }

                        var divCuerpo = driver.FindElements(By.XPath("//div[contains(@aria-label, 'Cuerpo') or contains(@aria-label, 'Body') and @role='textbox']")).FirstOrDefault();
                        if (divCuerpo != null)
                        {
                            string cuerpoHTML = DatabaseHelper.ObtenerConfiguracion("Email_Nombramientos", "Queridos hermanos,<br><br>Adjunto las cartas de nombramiento de la reciente visita.<br><br>Un fuerte abrazo.");
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].innerHTML = arguments[1];", divCuerpo, cuerpoHTML);
                        }

                        if (rutasPdfs.Count > 0)
                        {
                            var inputArchivo = driver.FindElements(By.XPath("//input[@type='file']")).FirstOrDefault();
                            if (inputArchivo != null)
                            {
                                string rutasJuntas = string.Join("\n", rutasPdfs);
                                inputArchivo.SendKeys(rutasJuntas);

                                System.Threading.Thread.Sleep(1000 * rutasPdfs.Count);

                                WebDriverWait waitSubida = new WebDriverWait(driver, TimeSpan.FromSeconds(90));

                                foreach (var ruta in rutasPdfs)
                                {
                                    string nombreCorto = Path.GetFileNameWithoutExtension(ruta);
                                    if (nombreCorto.Length > 15) nombreCorto = nombreCorto.Substring(0, 15);
                                    try { waitSubida.Until(d => d.PageSource.Contains(nombreCorto)); } catch { }
                                }

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
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message);
                }
            }
        }

        private static void LimpiarCarpetaTemporal(string ruta)
        {
            var di = new DirectoryInfo(ruta);
            foreach (FileInfo file in di.GetFiles())
            {
                try { file.Delete(); } catch { }
            }
        }

        private static void ProcesarArchivosDescargados(string rutaTemp, string nombreHermano, string categoriaTag)
        {
            string carpetaDestino = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchivosDocumentacion");
            if (!Directory.Exists(carpetaDestino)) Directory.CreateDirectory(carpetaDestino);

            var di = new DirectoryInfo(rutaTemp);
            foreach (FileInfo file in di.GetFiles())
            {
                if (file.Extension.EndsWith("part")) continue;

                string nuevoNombre = $"{nombreHermano} - {file.Name}";
                string nombreUnico = Guid.NewGuid().ToString().Substring(0, 5) + "_" + nuevoNombre;
                string rutaFinal = Path.Combine(carpetaDestino, nombreUnico);

                try
                {
                    file.MoveTo(rutaFinal);
                    DatabaseHelper.InsertarDocumento(categoriaTag, nuevoNombre, rutaFinal);
                }
                catch { }
            }
        }

        private static IWebElement EsperaDinamicaElementoVisibilidad(IWebDriver driver, By locator, int segundosMaximos)
        {
            try
            {
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(segundosMaximos));
                wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));
                return wait.Until(d =>
                {
                    try
                    {
                        var elements = d.FindElements(locator);
                        if (elements.Count > 0 && elements[0].Displayed) return elements[0];
                        return null;
                    }
                    catch (StaleElementReferenceException) { return null; }
                });
            }
            catch { return null; }
        }

        private static IWebElement EsperaDinamicaElementoClicable(IWebDriver driver, By locator, int segundosMaximos)
        {
            try
            {
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(segundosMaximos));
                wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));
                return wait.Until(d =>
                {
                    try
                    {
                        var elements = d.FindElements(locator);
                        if (elements.Count > 0 && elements[0].Displayed && elements[0].Enabled) return elements[0];
                        return null;
                    }
                    catch (StaleElementReferenceException) { return null; }
                });
            }
            catch { return null; }
        }

        private static string ConvertirFechaJWHub(string textoFecha)
        {
            if (string.IsNullOrWhiteSpace(textoFecha)) return "";
            string txt = textoFecha.ToLower().Replace(" de ", " ").Replace(".", "");

            var meses = new string[] { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };
            var mesesCompletos = new string[] { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

            int dia = 1;
            int mes = 1;
            int anio = 1900;

            var matchYear = System.Text.RegularExpressions.Regex.Match(txt, @"\b(19|20)\d{2}\b");
            if (matchYear.Success) anio = int.Parse(matchYear.Value);
            else return textoFecha;

            bool mesEncontrado = false;
            for (int i = 0; i < 12; i++)
            {
                if (txt.Contains(meses[i]) || txt.Contains(mesesCompletos[i]))
                {
                    mes = i + 1;
                    mesEncontrado = true;
                    break;
                }
            }

            string txtSinAnio = txt.Replace(anio.ToString(), "");
            var matchDay = System.Text.RegularExpressions.Regex.Match(txtSinAnio, @"\b([1-9]|[12][0-9]|3[01])\b");
            if (matchDay.Success) dia = int.Parse(matchDay.Value);

            return $"{dia:D2}/{mes:D2}/{anio}";
        }

        private static string ExtraerCodigoCongregacion(string textoOriginal)
        {
            if (string.IsNullOrWhiteSpace(textoOriginal)) return "";
            int inicio = textoOriginal.LastIndexOf('(');
            int fin = textoOriginal.LastIndexOf(')');
            if (inicio >= 0 && fin > inicio) return textoOriginal.Substring(inicio + 1, fin - inicio - 1).Trim();
            return textoOriginal.Trim();
        }

        private static string ExtraerCodigosSolicitudes(string textoOriginal)
        {
            if (string.IsNullOrWhiteSpace(textoOriginal)) return "";
            var coincidencias = System.Text.RegularExpressions.Regex.Matches(textoOriginal, @"[A-Z]+-\d+");
            if (coincidencias.Count > 0)
            {
                var codigos = new List<string>();
                foreach (System.Text.RegularExpressions.Match match in coincidencias) codigos.Add(match.Value);
                return string.Join(", ", codigos);
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(textoOriginal.Trim(), @"^\d+$")) return "";
            return textoOriginal.Trim();
        }
    }

    public class PersonaExtraida
    {
        public string NombreCompleto { get; set; }
        public string NombreComun { get; set; }
        public string Apellidos { get; set; }

        // --- PROPIEDADES DE FECHA DE PRECURSORADO ---
        public string FechaInicioPrec { get; set; }
        // Esta propiedad puente evita que el resto del programa (DatabaseHelper, EmparejarPersonas) dé error
        public string FechaInicio { get => FechaInicioPrec; set => FechaInicioPrec = value; }

        public string FechaUltimaEscuela { get; set; }
        public string FechaBautismo { get; set; }
        public string Email { get; set; }
        public string EmailJW { get; set; }
        public string Telefono { get; set; }
        public string Direccion { get; set; }
        public string Congregacion { get; set; }
        public string Servicio { get; set; }
        public string Nombramiento { get; set; }
        public string Solicitudes { get; set; }
        public string Privilegios { get; set; }
        public string Foto { get; set; }
        public string FechaNacimiento { get; set; }
        public string Esperanza { get; set; }
    }
}