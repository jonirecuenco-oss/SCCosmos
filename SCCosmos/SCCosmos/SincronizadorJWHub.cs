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
        // =========================================================================
        // VARIABLES GLOBALES DE ESTADO (Para avisar a la interfaz gráfica)
        // =========================================================================
        public static bool EstaSincronizando { get; private set; }
        public static event Action<bool> SincronizacionCambiada;

        // NUEVO: Evento para enviar (NumeroActual, Total)
        public static event Action<int, int> ProgresoCambiado;

        public static List<PersonaExtraida> SincronizarGlobal(string rutaPerfilFirefox, bool modoSilencioso = false)
        {
            var listaExtraidos = new List<PersonaExtraida>();

            // BLOQUEO DE SEGURIDAD: Si ya se está ejecutando, ignoramos esta llamada
            if (EstaSincronizando) return listaExtraidos;

            // Avisamos a todo el programa de que EMPIEZA la sincronización
            EstaSincronizando = true;
            SincronizacionCambiada?.Invoke(true);

            // 1. CLONAMOS EL PERFIL
            string perfilClonado = ClonarPerfilFirefox(rutaPerfilFirefox, "_TempSync");

            try
            {
                string urlBase = "https://hub.jw.org";

                // Cargamos la BD en memoria para la validación cruzada anti-conflictos
                var personasDB = DatabaseHelper.ObtenerPersonas().Where(p => string.IsNullOrEmpty(p.FechaEliminacion)).ToList();

                var service = FirefoxDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                FirefoxOptions options = new FirefoxOptions();
                options.AddArgument("-profile");
                options.AddArgument(perfilClonado); // USAMOS EL CLON

                // Oculto y a pantalla completa virtual
                options.AddArgument("-headless");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--start-maximized");

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

                        string urlPersonas = urlBase + "/congregation-persons/es/circuits/512bc4a5-94aa-ea11-815e-0050560012f5/records";
                        driver.Navigate().GoToUrl(urlPersonas);

                        //MODO DE PRUEBAS
                        
                        /*var inputBuscador = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//input[@type='search' and @placeholder='Introduzca un nombre']"), 15);
                        if (inputBuscador != null)
                        {
                            inputBuscador.Clear();
                            inputBuscador.SendKeys("guzmán, rosa julia");
                            Thread.Sleep(500); // Pequeña pausa para que Angular registre el texto tecleado
                        }*/
                        
                        //int totalCards = 2;
                        //FIN DEL MODO DE PRUEBAS

                        var btnBuscar = EsperaDinamicaElementoClicable(driver, By.XPath("//button[.//span[contains(text(), 'Buscar')]]"), 15);
                        if (btnBuscar != null)
                        {
                            Thread.Sleep(1000);
                            btnBuscar.Click();
                        }

                        EsperaDinamicaElementoVisibilidad(driver, By.CssSelector("article"), 15);

                        // Mostrar más. Después de cada clic esperamos a que Angular
                        // realmente incorpore las nuevas tarjetas antes de continuar.
                        while (true)
                        {
                            var btnMostrarMas = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//button[contains(@class, 'margin-top-05') and .//ptrn-translate[contains(text(), 'Mostrar') and contains(text(), 'más')]]"), 3);
                            if (btnMostrarMas != null && btnMostrarMas.Displayed)
                            {
                                int antes = ObtenerTarjetasPersona(driver).Count;
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnMostrarMas);
                                Thread.Sleep(200);
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnMostrarMas);
                                EsperarCambioEnListadoPersonas(driver, antes, 5);
                            }
                            else break;
                        }

                        // Mostrar todo
                        var btnMostrarTodo = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//button[.//ptrn-translate[contains(text(), 'Mostrar todo')]]"), 3);
                        if (btnMostrarTodo != null && btnMostrarTodo.Displayed)
                        {
                            int antes = ObtenerTarjetasPersona(driver).Count;
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnMostrarTodo);
                            Thread.Sleep(200);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnMostrarTodo);
                            EsperarCambioEnListadoPersonas(driver, antes, 10);
                        }

                        // Esperamos a que la SPA deje de reordenar / hidratar el listado.
                        // Solo contamos article que realmente son fichas de personas.
                        EsperarListadoPersonasEstable(driver, 20);
                        var cardsBase = ObtenerTarjetasPersona(driver);
                        int totalCards = cardsBase.Count;

                        //MODO DE PRUEBAS
                        /*
                        var inputBuscador = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//input[@type='search' and @placeholder='Introduzca un nombre']"), 15);
                        if (inputBuscador != null)
                        {
                            inputBuscador.Clear();
                            inputBuscador.SendKeys("gonzález, miriam");
                            Thread.Sleep(500); // Pequeña pausa para que Angular registre el texto tecleado
                        }
                        var btnBuscarP = EsperaDinamicaElementoClicable(driver, By.XPath("//button[.//span[contains(text(), 'Buscar')]]"), 15);
                        if (btnBuscarP != null)
                        {
                            Thread.Sleep(1000);
                            btnBuscarP.Click();
                        }
                        //int totalCards = 2;
                        */
                        //FIN DEL MODO DE PRUEBAS


                        string[] privilegiosPermitidos = {
                            "Instructor de la Escuela del Servicio de Precursor", "Superintendente de circuito sustituto",
                            "Miembro del CEH", "Superintendente de asamblea", "Coordinador", "Secretario",
                            "Miembro del GVP", "Persona que pertenece a una entidad del LDC", "Superintendente auxiliar de asamblea"
                        };

                        int reintentosFallidos = 0;
                        int i = 0;

                        while (i < totalCards && reintentosFallidos < 5)
                        {
                            // Reobtenemos las tarjetas en cada vuelta para no conservar
                            // IWebElement obsoletos después de abrir/cerrar perfiles.
                            var cards = ObtenerTarjetasPersona(driver);
                            totalCards = Math.Max(totalCards, cards.Count);

                            if (i >= cards.Count)
                            {
                                EsperarListadoPersonasEstable(driver, 8);
                                cards = ObtenerTarjetasPersona(driver);
                                totalCards = cards.Count;
                            }

                            if (i >= cards.Count) break;
                            var card = cards[i];

                            var spanNombre = card.FindElements(By.CssSelector("span.transliteration__vernacular")).FirstOrDefault();

                            // No saltamos una tarjeta todavía no hidratada: ese era uno
                            // de los motivos por los que la primera persona podía acabar
                            // figurando como baja. La reintentamos antes de avanzar.
                            if (spanNombre == null)
                            {
                                reintentosFallidos++;
                                Thread.Sleep(500);
                                continue;
                            }

                            string nombreReal = (spanNombre.GetAttribute("textContent") ?? "")
                                .Replace("\u200B", "")
                                .Trim();

                            if (string.IsNullOrWhiteSpace(nombreReal))
                            {
                                reintentosFallidos++;
                                Thread.Sleep(500);
                                continue;
                            }

                            reintentosFallidos = 0;

                            PersonaExtraida mejorExtraccion = null;

                            // Bucle de revisiones: Hasta 5 para errores graves (críticos), máximo 3 para conflictos de datos.
                            for (int revision = 1; revision <= 5; revision++)
                            {
                                try
                                {
                                    string urlActual = driver.Url;
                                    if (urlActual.Contains("/error", StringComparison.OrdinalIgnoreCase))
                                        throw new Exception("Error 404/500 devuelto por JW Hub.");
                                }
                                catch (WebDriverException) { throw new Exception("Pérdida de conexión con el navegador."); }

                                try
                                {
                                    // NombreJWHub conserva SIEMPRE el texto real leído en Hub.
                                    // NombreCompleto y NombreComun se resolverán después de identificar
                                    // a la persona en BBDD: si ya existen, no se recalculan ni se pisan.
                                    var tempPersona = new PersonaExtraida
                                    {
                                        NombreJWHub = nombreReal,
                                        NombreCompleto = nombreReal
                                    };

                                    var badges = card.FindElements(By.CssSelector("ptrn-badges .badge"));
                                    foreach (var badge in badges)
                                    {
                                        string textoBadge = badge.GetAttribute("textContent").Trim();
                                        if (textoBadge.Contains("Misionero", StringComparison.OrdinalIgnoreCase)) tempPersona.Servicio = "Misionero";
                                        else if (textoBadge.Contains("Precursor especial", StringComparison.OrdinalIgnoreCase)) tempPersona.Servicio = "Precursor especial";
                                        else if (textoBadge.Contains("Precursor", StringComparison.OrdinalIgnoreCase)) { if (tempPersona.Servicio != "Precursor especial") tempPersona.Servicio = "Precursor regular"; }
                                        else if (textoBadge.Contains("Anciano", StringComparison.OrdinalIgnoreCase)) tempPersona.Nombramiento = "Anciano";
                                        else if (textoBadge.Contains("Siervo", StringComparison.OrdinalIgnoreCase)) tempPersona.Nombramiento = "Siervo ministerial";
                                    }

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
                                            if (dds.Count > 0) tempPersona.Congregacion = string.Join(", ", dds.Select(d => d.GetAttribute("textContent").Trim()));
                                        }
                                        else if (etiqueta.Contains("edad") || etiqueta.Contains("nacimiento"))
                                        {
                                            var dds = par.FindElements(By.TagName("dd"));
                                            if (dds.Count > 0)
                                            {
                                                string valorTxt = dds.First().GetAttribute("textContent").Replace("\u200B", "").Trim();
                                                if (valorTxt.Contains("(")) valorTxt = valorTxt.Substring(0, valorTxt.IndexOf("(")).Trim();

                                                var matchYear = System.Text.RegularExpressions.Regex.Match(valorTxt, @"\b(19|20)\d{2}\b");
                                                if (matchYear.Success) tempPersona.FechaNacimiento = ConvertirFechaJWHub(valorTxt);
                                                else
                                                {
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
                                                string valorTxt = dds.First().GetAttribute("textContent").Replace("\u200B", "").Trim();
                                                if (valorTxt.Contains("(")) valorTxt = valorTxt.Substring(0, valorTxt.IndexOf("(")).Trim();

                                                var matchYear = System.Text.RegularExpressions.Regex.Match(valorTxt, @"\b(19|20)\d{2}\b");
                                                if (matchYear.Success) tempPersona.FechaBautismo = ConvertirFechaJWHub(valorTxt);
                                                else
                                                {
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
                                                if ((tlfRaw.StartsWith("6") || tlfRaw.StartsWith("7")) && string.IsNullOrEmpty(tempPersona.Telefono)) tempPersona.Telefono = tlfRaw;
                                            }
                                        }
                                    }

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
                                                            finally { driver.Close(); driver.SwitchTo().Window(ventanaPerfil); }
                                                        }
                                                    }
                                                }

                                                var emails = driver.FindElements(By.CssSelector("bdi.email"));
                                                foreach (var email in emails)
                                                {
                                                    string htmlTag = email.GetAttribute("outerHTML");
                                                    if (htmlTag.Contains("ng-star-inserted") && !htmlTag.Contains("@")) continue;

                                                    string textoEmail = email.GetAttribute("textContent").Trim();
                                                    if (textoEmail.EndsWith("@jwpub.org", StringComparison.OrdinalIgnoreCase)) tempPersona.EmailJW = textoEmail;
                                                    else if (!string.IsNullOrEmpty(textoEmail) && textoEmail.Contains("@")) tempPersona.Email = textoEmail;
                                                }

                                                var appAddress = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'dirección física') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'dirección postal')]/following-sibling::dd//app-address")).FirstOrDefault();
                                                if (appAddress != null)
                                                {
                                                    string dirRaw = appAddress.GetAttribute("textContent").Trim();
                                                    tempPersona.Direccion = string.Join(", ", dirRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
                                                }

                                                // 1. INFORMACIÓN PERSONAL
                                                // JW Hub muestra "Masculino" / "Femenino".
                                                // SCCosmos normaliza esos valores a "Hombre" / "Mujer".
                                                ExtraerSexoInformacionPersonal(driver, tempPersona);

                                                // 2. LINK ESTADO
                                                var btnEstado = EsperaDinamicaElementoClicable(driver, By.XPath("//a[contains(@href, '/spiritual-status') or contains(@href, '/status') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'estado')]"), 3);
                                                if (btnEstado != null)
                                                {
                                                    btnEstado.Click();
                                                    Thread.Sleep(2000);

                                                    var ddEsperanza = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'esperanza')]/following-sibling::dd")).FirstOrDefault();
                                                    if (ddEsperanza != null)
                                                    {
                                                        string txtEsp = ddEsperanza.GetAttribute("textContent").Trim();
                                                        if (txtEsp.Contains("Otras", StringComparison.OrdinalIgnoreCase)) tempPersona.Esperanza = "Otras ovejas";
                                                        else if (txtEsp.Contains("Ungido", StringComparison.OrdinalIgnoreCase)) tempPersona.Esperanza = "Ungido";
                                                    }

                                                    if (string.IsNullOrEmpty(tempPersona.FechaInicioPrec))
                                                    {
                                                        var ddInicio = driver.FindElements(By.XPath("//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'inicio del precursorado') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'tiempo completo')]/following-sibling::dd")).FirstOrDefault();
                                                        if (ddInicio != null)
                                                        {
                                                            string textoCrudo = ddInicio.GetAttribute("textContent").Replace("\u200B", "").Trim();
                                                            if (textoCrudo.Contains("(")) textoCrudo = textoCrudo.Substring(0, textoCrudo.IndexOf("(")).Trim();

                                                            string parsedDate = ConvertirFechaJWHub(textoCrudo);
                                                            if (!string.IsNullOrEmpty(parsedDate))
                                                            {
                                                                tempPersona.FechaInicioPrec = parsedDate;
                                                            }
                                                        }
                                                    }
                                                }

                                                // 3. LINK SOLICITUDES
                                                var btnSolicitudes = EsperaDinamicaElementoClicable(driver, By.XPath("//a[contains(@href, '/applications') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'solicitud')]"), 3);
                                                if (btnSolicitudes != null)
                                                {
                                                    btnSolicitudes.Click();
                                                    Thread.Sleep(2000);
                                                    string htmlSols = driver.PageSource;
                                                    var matches = System.Text.RegularExpressions.Regex.Matches(htmlSols, @"\b(A-2|A-8|G-8|A-19)\b");
                                                    var solList = new List<string>();
                                                    foreach (System.Text.RegularExpressions.Match m in matches) solList.Add(m.Value);
                                                    if (solList.Count > 0) tempPersona.Solicitudes = string.Join(", ", solList.Distinct());
                                                }

                                                // 4. LINK EDUCACIÓN
                                                var btnEducacion = EsperaDinamicaElementoClicable(driver, By.XPath("//a[contains(@href, '/theocratic-education') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'educaci')]"), 3);
                                                if (btnEducacion != null)
                                                {
                                                    btnEducacion.Click();
                                                    Thread.Sleep(2000);
                                                    var headerEscuela = driver.FindElements(By.XPath("//h2[contains(., 'Pioneer Service School') or contains(., 'Escuela del Servicio de Precursor')]")).FirstOrDefault();
                                                    if (headerEscuela != null)
                                                    {
                                                        var cardEscuela = headerEscuela.FindElement(By.XPath("./ancestor::article[contains(@class, 'card')]"));
                                                        var ddGrad = cardEscuela.FindElements(By.XPath(".//dt[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'fecha de graduación')]/following-sibling::dd")).FirstOrDefault();
                                                        if (ddGrad != null)
                                                        {
                                                            string textoGrad = ddGrad.GetAttribute("textContent").Replace("\u200B", "").Trim();
                                                            if (textoGrad.Contains("(")) textoGrad = textoGrad.Substring(0, textoGrad.IndexOf("(")).Trim();
                                                            tempPersona.FechaUltimaEscuela = ConvertirFechaJWHub(textoGrad);
                                                        }
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                driver.Close();
                                                driver.SwitchTo().Window(ventanaListado);
                                                Thread.Sleep(1000);
                                            }
                                        }
                                    }

                                    // =========================================================
                                    // IDENTIFICAR PRIMERO LA FICHA ACTUAL DE BBDD
                                    // =========================================================
                                    string codigoDet = "";
                                    if (!string.IsNullOrWhiteSpace(tempPersona.Congregacion))
                                    {
                                        int start = tempPersona.Congregacion.LastIndexOf('(');
                                        int end = tempPersona.Congregacion.LastIndexOf(')');
                                        if (start >= 0 && end > start) codigoDet = tempPersona.Congregacion.Substring(start + 1, end - start - 1).Trim();
                                    }

                                    var matchDB = personasDB.FirstOrDefault(p =>
                                    {
                                        // Coincidencia de identidad: el nombre de Hub se usa para localizar,
                                        // pero NO para sustituir Nombre/NombreComun ya consolidados.
                                        bool mismoNombre =
                                            (!string.IsNullOrEmpty(p.NombreJWHub) &&
                                             string.Equals(p.NombreJWHub, nombreReal, StringComparison.OrdinalIgnoreCase)) ||
                                            (!string.IsNullOrEmpty(p.Nombre) &&
                                             string.Equals(p.Nombre, nombreReal, StringComparison.OrdinalIgnoreCase));

                                        bool mismaCongre = p.CongregacionCodigo == codigoDet;
                                        if (!mismoNombre || !mismaCongre) return false;

                                        // Filtro anti-homónimos (padre/hijo). Una fecha 01/01/YYYY
                                        // procedente de edad/años bautizado es aproximada.
                                        Func<string, string, bool> SonIncompatibles = (dbDate, hubDate) =>
                                        {
                                            if (string.IsNullOrWhiteSpace(dbDate) || string.IsNullOrWhiteSpace(hubDate)) return false;

                                            bool dbAprox = dbDate.StartsWith("01/01/");
                                            bool hubAprox = hubDate.StartsWith("01/01/");

                                            if (!dbAprox && !hubAprox) return dbDate.Trim() != hubDate.Trim();

                                            int yearDB = 0, yearHub = 0;
                                            if (dbDate.Length >= 4) int.TryParse(dbDate.Substring(dbDate.Length - 4), out yearDB);
                                            if (hubDate.Length >= 4) int.TryParse(hubDate.Substring(hubDate.Length - 4), out yearHub);

                                            return yearDB > 0 && yearHub > 0 && Math.Abs(yearDB - yearHub) > 2;
                                        };

                                        if (SonIncompatibles(p.FechaNacimiento, tempPersona.FechaNacimiento)) return false;
                                        if (SonIncompatibles(p.FechaBautismo, tempPersona.FechaBautismo)) return false;

                                        return true;
                                    });

                                    // NOMBRES: si la ficha actual ya los tiene, se conservan tal cual.
                                    // Solo calculamos valores de Hub cuando realmente faltan en BBDD.
                                    AplicarNombresConservadores(tempPersona, matchDB, nombreReal);

                                    if (matchDB != null)
                                    {
                                        // FECHAS DEFINITIVAS DEL S-21/JSON:
                                        // si la persona tiene origen S21, nacimiento y bautismo de BBDD
                                        // prevalecen SIEMPRE sobre cálculos de edad/años bautizado de Hub.
                                        if (matchDB.TieneOrigenS21 && !string.IsNullOrWhiteSpace(matchDB.FechaNacimiento))
                                            tempPersona.FechaNacimiento = matchDB.FechaNacimiento;
                                        else if (!string.IsNullOrEmpty(tempPersona.FechaNacimiento) && tempPersona.FechaNacimiento.StartsWith("01/01/") && !string.IsNullOrEmpty(matchDB.FechaNacimiento))
                                            tempPersona.FechaNacimiento = matchDB.FechaNacimiento;

                                        if (matchDB.TieneOrigenS21 && !string.IsNullOrWhiteSpace(matchDB.FechaBautismo))
                                            tempPersona.FechaBautismo = matchDB.FechaBautismo;
                                        else if (!string.IsNullOrEmpty(tempPersona.FechaBautismo) && tempPersona.FechaBautismo.StartsWith("01/01/") && !string.IsNullOrEmpty(matchDB.FechaBautismo))
                                            tempPersona.FechaBautismo = matchDB.FechaBautismo;

                                        // Inicio de precursorado mantiene la protección previa para fechas aproximadas.
                                        if (!string.IsNullOrEmpty(tempPersona.FechaInicioPrec) && tempPersona.FechaInicioPrec.StartsWith("01/01/") && !string.IsNullOrEmpty(matchDB.FechaInicioPrec))
                                            tempPersona.FechaInicioPrec = matchDB.FechaInicioPrec;
                                    }

                                    // =========================================================
                                    // VALIDACIÓN CRÍTICA
                                    // =========================================================
                                    bool faltanDatosGrave = string.IsNullOrWhiteSpace(tempPersona.Congregacion) ||
                                                            string.IsNullOrWhiteSpace(tempPersona.FechaNacimiento) ||
                                                            string.IsNullOrWhiteSpace(tempPersona.FechaBautismo);

                                    if (faltanDatosGrave)
                                    {
                                        if (revision >= 5) break;
                                        throw new Exception("Faltan datos obligatorios. Forzando reintento crítico...");
                                    }

                                    // Guardamos la mejor extracción antes de evaluar cambios no críticos.
                                    mejorExtraccion = tempPersona;

                                    if (matchDB != null)
                                    {
                                        bool hayConflicto = DetectarConflictoSincronizador(matchDB, tempPersona, codigoDet);
                                        if (hayConflicto)
                                        {
                                            if (revision >= 3) break;
                                            throw new Exception("Posible actualización de datos detectada. Forzando re-evaluación (Intento " + revision + "/3)...");
                                        }
                                    }

                                    // Si ha llegado hasta aquí, la extracción ha sido perfecta
                                    break;
                                }
                                catch (Exception)
                                {
                                    Thread.Sleep(1500);
                                    cards = ObtenerTarjetasPersona(driver);
                                    totalCards = cards.Count; // Mantenemos el total actualizado
                                    if (i < cards.Count) card = cards[i];
                                }
                            }

                            // AÑADIMOS A LA PERSONA A LA LISTA SÓLO SI SUPERÓ LAS PRUEBAS
                            if (mejorExtraccion != null)
                            {
                                listaExtraidos.Add(mejorExtraccion);
                            }

                            i++;
                            reintentosFallidos = 0;

                            ProgresoCambiado?.Invoke(i, totalCards);
                        }

                        // =========================================================================
                        // FASE 2: EXTRAER NOMBRAMIENTOS Y DESCARGAR ARCHIVOS
                        // =========================================================================
                        string urlNombramientos = urlBase + "/congregation-persons/es/circuits/512bc4a5-94aa-ea11-815e-0050560012f5/appointments";
                        driver.Navigate().GoToUrl(urlNombramientos);
                        Thread.Sleep(5000);

                        // La página de recomendaciones debe haber terminado de cargar antes
                        // de sincronizar la BBDD. Si Hub devuelve una página de error, no
                        // tocamos los registros locales.
                        bool paginaNombramientosValida = EsperarPaginaNombramientosValida(driver, 15);
                        string htmlNombramientos = driver.PageSource;

                        if (Type.GetType("SCCosmos.ExtractorNombramientosHelper") != null)
                        {
                            var recomendaciones = ExtractorNombramientosHelper.ExtraerRecomendaciones(htmlNombramientos);

                            if (paginaNombramientosValida)
                            {
                                // GuardarRecomendaciones funciona ahora como espejo de Hub:
                                // también elimina de BBDD las recomendaciones que ya no están,
                                // incluido el caso de que actualmente no haya ninguna.
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
                    }
                    catch (Exception)
                    {
                        if (!modoSilencioso)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                System.Windows.MessageBox.Show($"La extracción se ha detenido en el publicador {listaExtraidos.Count} debido a una desconexión o fallo en JW Hub.\n\nEl sistema abrirá la ventana para que puedas guardar el progreso de los hermanos que sí se han logrado extraer de forma segura.", "Aviso de Sincronización Parcial", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                            });
                        }
                    }
                }
            }
            finally
            {
                EstaSincronizando = false;
                SincronizacionCambiada?.Invoke(false);
                EliminarPerfilClonado(perfilClonado);
            }

            return listaExtraidos;
        }

        // Solo cambia el estado en Hub: NO genera mensajes individuales.
        // El envío único del S-303 y, cuando corresponde, la carta, pertenece a VisitasPage.
        public static void AprobarRecomendacionHubSinCorreo(
            string rutaPerfilFirefox, RecomendacionHub rec, DateTime fechaAprobacion)
        {
            if (rec == null) throw new ArgumentNullException(nameof(rec));
            string urlDetalle = ValidarUrlRecomendacion(rec.UrlDetalle);
            string perfilClonado = ClonarPerfilFirefox(rutaPerfilFirefox, "_TempApproveS303");
            try
            {
                var service = FirefoxDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;
                var options = new FirefoxOptions();
                options.AddArgument("-profile");
                options.AddArgument(perfilClonado);
                options.AddArgument("-headless");
                options.AddArgument("--window-size=1920,1080");

                using (IWebDriver driver = new FirefoxDriver(service, options))
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
                    driver.Navigate().GoToUrl(urlDetalle);
                    IWebElement btnAnalizar = EsperaDinamicaElementoVisibilidad(driver,
                        By.XPath("//a[.//ptrn-translate[contains(., 'Analizar hermano')]] | //*[contains(@class, 'button')]//ptrn-translate[contains(., 'Analizar hermano')]/ancestor::a"), 20);
                    if (btnAnalizar == null) throw new InvalidOperationException(
                        "No se encuentra 'Analizar hermano' en la recomendación de " + rec.Hermano);
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnAnalizar);

                    IWebElement btnEditar = EsperaDinamicaElementoVisibilidad(driver,
                        By.XPath("//a[.//ptrn-translate[contains(., 'Editar estado del nombramiento')]]"), 20);
                    if (btnEditar == null) throw new InvalidOperationException(
                        "No se encuentra 'Editar estado del nombramiento' de " + rec.Hermano);
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEditar);

                    // El selector se apoya en el flujo que ya usa PersonasPage.
                    IWebElement lblAprobar = EsperaDinamicaElementoVisibilidad(driver,
                        By.XPath("//label[.//ptrn-translate[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'aprobar') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'confirmar')]]"), 12);
                    if (lblAprobar == null) throw new InvalidOperationException(
                        "No se encuentra la opción de aprobar/confirmar para " + rec.Hermano);
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", lblAprobar);

                    var txtFecha = driver.FindElements(By.Id("form.effectiveDate")).FirstOrDefault()
                        ?? driver.FindElements(By.XPath("//input[@placeholder='d/M/yy']")).FirstOrDefault();
                    if (txtFecha != null)
                    {
                        txtFecha.Clear();
                        txtFecha.SendKeys(fechaAprobacion.ToString("d/M/yy"));
                        txtFecha.SendKeys(Keys.Tab);
                    }

                    IWebElement btnGuardar = EsperaDinamicaElementoVisibilidad(driver,
                        By.XPath("//button[.//ptrn-translate[contains(., 'Guardar')]]"), 12);
                    if (btnGuardar == null) throw new InvalidOperationException(
                        "No se encuentra Guardar al aprobar a " + rec.Hermano);
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnGuardar);
                    Thread.Sleep(1000);

                    IWebElement btnEnviar = EsperaDinamicaElementoVisibilidad(driver,
                        By.XPath("//*[.//ptrn-translate[contains(., 'Enviar')]]/ancestor-or-self::button | //*[.//ptrn-translate[contains(., 'Enviar')]]/ancestor-or-self::a"), 15);
                    if (btnEnviar == null) throw new InvalidOperationException(
                        "No se encuentra Enviar/confirmar aprobación para " + rec.Hermano);
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);
                    Thread.Sleep(3000);
                    VerificarPaginaHubSinError(driver, rec.Hermano);
                }
            }
            finally
            {
                EliminarPerfilClonado(perfilClonado);
            }
        }

        private static string ValidarUrlRecomendacion(string ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta))
                throw new InvalidOperationException("La recomendación no contiene URL de detalle en la base de datos.");
            Uri url;
            if (!Uri.TryCreate(new Uri("https://hub.jw.org"), ruta, out url)
                || !string.Equals(url.Host, "hub.jw.org", StringComparison.OrdinalIgnoreCase)
                || url.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("La URL de recomendación no pertenece a hub.jw.org.");
            }
            return url.AbsoluteUri;
        }

        private static void VerificarPaginaHubSinError(IWebDriver driver, string hermano)
        {
            var url = driver.Url ?? "";
            if (url.Contains("/error") || url.Contains("/login") || url.Contains("/signin") ||
                driver.PageSource.Contains("414 ERROR"))
            {
                throw new InvalidOperationException(
                    "JW Hub ha mostrado una página de error o acceso al procesar a " + hermano);
            }
        }

        public static void CancelarRecomendacionHub(string rutaPerfilFirefox, RecomendacionHub rec)
        {
            string perfilClonado = ClonarPerfilFirefox(rutaPerfilFirefox, "_TempCancel");

            try
            {
                string urlDetalle = ValidarUrlRecomendacion(rec.UrlDetalle);
                var service = FirefoxDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                FirefoxOptions options = new FirefoxOptions();
                options.AddArgument("-profile");
                options.AddArgument(perfilClonado);
                options.AddArgument("-headless");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--start-maximized");

                using (IWebDriver driver = new FirefoxDriver(service, options))
                {
                    try
                    {
                        driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
                        driver.Navigate().GoToUrl(urlDetalle);
                        Thread.Sleep(5000);

                        var btnAnalizar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//a[.//ptrn-translate[contains(., 'Analizar hermano')]] | //*[contains(@class, 'button')]//ptrn-translate[contains(., 'Analizar hermano')]/ancestor::a"), 10);
                        if (btnAnalizar == null) throw new InvalidOperationException("No se encuentra 'Analizar hermano' para " + rec.Hermano);
                        if (btnAnalizar != null)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnAnalizar);
                            Thread.Sleep(500);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnAnalizar);
                            Thread.Sleep(4000);

                            var btnEditar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//a[.//ptrn-translate[contains(., 'Editar estado del nombramiento')]]"), 10);
                            if (btnEditar == null) throw new InvalidOperationException("No se encuentra el editor de estado para " + rec.Hermano);
                            if (btnEditar != null)
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnEditar);
                                Thread.Sleep(500);
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEditar);
                                Thread.Sleep(3000);

                                var lblCancelar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//label[.//ptrn-translate[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'cancelar')]]"), 5);
                                if (lblCancelar == null) throw new InvalidOperationException("No se encuentra la opción Cancelar para " + rec.Hermano);
                                if (lblCancelar != null)
                                {
                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", lblCancelar);
                                    Thread.Sleep(1000);
                                }

                                var btnGuardar = driver.FindElements(By.XPath("//button[.//ptrn-translate[contains(., 'Guardar')]]")).FirstOrDefault();
                                if (btnGuardar == null) throw new InvalidOperationException("No se encuentra Guardar para " + rec.Hermano);
                                if (btnGuardar != null)
                                {
                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnGuardar);
                                    Thread.Sleep(5000);
                                }
                            }
                        }

                        var btnEnviar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//*[.//ptrn-translate[contains(., 'Enviar')]]/ancestor-or-self::button | //*[.//ptrn-translate[contains(., 'Enviar')]]/ancestor-or-self::a"), 10);
                        if (btnEnviar == null) throw new InvalidOperationException("No se encuentra Enviar para cancelar la recomendación de " + rec.Hermano);
                        if (btnEnviar != null)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnEnviar);
                            Thread.Sleep(500);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);
                            Thread.Sleep(3000);
                        }
                        VerificarPaginaHubSinError(driver, rec.Hermano);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message);
                    }
                }
            }
            finally
            {
                EliminarPerfilClonado(perfilClonado);
            }
        }

        public static void AprobarRecomendacionYEnviarCorreo(string rutaPerfilFirefox, RecomendacionHub rec, DateTime fechaAprobacion, List<string> rutasPdfs)
        {
            string perfilClonado = ClonarPerfilFirefox(rutaPerfilFirefox, "_TempApprove");

            try
            {
                string urlBase = "https://hub.jw.org";
                var service = FirefoxDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                FirefoxOptions options = new FirefoxOptions();
                options.AddArgument("-profile");
                options.AddArgument(perfilClonado);
                options.AddArgument("-headless");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--start-maximized");

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

                                var lblAprobar = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//label[.//ptrn-translate[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'aprobar') or contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'confirmar')]]"), 5);
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
            finally
            {
                EliminarPerfilClonado(perfilClonado);
            }
        }

        // =========================================================================
        // MÉTODOS DE RESOLUCIÓN Y UTILIDADES
        // =========================================================================

        private static void AplicarNombresConservadores(PersonaExtraida destino, Persona existente, string nombreHub)
        {
            destino.NombreJWHub = nombreHub ?? "";

            // Nombre completo: si BBDD ya lo conoce (por ejemplo, desde S-21),
            // no lo recalculamos desde Hub.
            destino.NombreCompleto = existente != null && !string.IsNullOrWhiteSpace(existente.Nombre)
                ? existente.Nombre
                : (nombreHub ?? "").Trim();

            // Nombre común: si ya existe, se conserva. Solo se deriva del nombre
            // de Hub cuando el campo está realmente vacío.
            if (existente != null && !string.IsNullOrWhiteSpace(existente.NombreComun))
            {
                destino.NombreComun = existente.NombreComun;
                destino.Apellidos = "";
                return;
            }

            string nombre = (nombreHub ?? "").Trim();
            string[] partes = nombre.Split(',');

            if (partes.Length >= 2)
            {
                string apellidos = partes[0].Trim();
                string nombrePila = partes[1].Trim();
                destino.NombreComun = $"{nombrePila} {apellidos}".Trim();
                destino.Apellidos = apellidos;
            }
            else
            {
                destino.NombreComun = nombre;
                destino.Apellidos = "";
            }
        }

        /// <summary>
        /// Compara dos textos ignorando espacios sobrantes (al principio, al
        /// final y repetidos por dentro). Así no se marcan como conflicto
        /// cambios irreales cuando el mismo dato llega con un espacio de más.
        /// </summary>
        private static bool MismoTexto(string a, string b)
        {
            return string.Equals(
                NormalizarTexto(a),
                NormalizarTexto(b),
                StringComparison.Ordinal);
        }

        private static bool MismoTextoSinMayusculas(string a, string b)
        {
            return string.Equals(
                NormalizarTexto(a),
                NormalizarTexto(b),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";

            var resultado = new System.Text.StringBuilder(texto.Length);
            bool huecoPendiente = false;

            foreach (char caracter in texto.Trim())
            {
                if (char.IsWhiteSpace(caracter))
                {
                    huecoPendiente = true;
                    continue;
                }

                if (huecoPendiente && resultado.Length > 0)
                {
                    resultado.Append(' ');
                }

                huecoPendiente = false;
                resultado.Append(caracter);
            }

            return resultado.ToString();
        }

        private static bool DetectarConflictoSincronizador(Persona db, PersonaExtraida hub, string codigoDet)
        {
            // El texto real de Hub se compara únicamente con NombreJWHub.
            if (!string.IsNullOrEmpty(hub.NombreJWHub) && !MismoTexto(db.NombreJWHub, hub.NombreJWHub)) return true;

            // Nombre y NombreComun ya rellenos en BBDD se consideran consolidados.
            // Hub solo debe aportarlos cuando estén vacíos.
            if (string.IsNullOrWhiteSpace(db.Nombre) && !string.IsNullOrWhiteSpace(hub.NombreCompleto)) return true;
            if (string.IsNullOrWhiteSpace(db.NombreComun) && !string.IsNullOrWhiteSpace(hub.NombreComun)) return true;

            // Grupo sigue siendo propio del S-21 / gestión local.
            // Sexo sí se obtiene ahora de "Información personal" de JW Hub.
            if (!string.IsNullOrWhiteSpace(hub.Sexo)
                && !string.Equals(db.Sexo ?? "", hub.Sexo, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(codigoDet) && !MismoTexto(db.CongregacionCodigo, codigoDet)) return true;

            string hubEsp = hub.Esperanza ?? "";
            string dbEsp = db.Esperanza ?? "";
            if (hubEsp != "" && !MismoTextoSinMayusculas(dbEsp, hubEsp)) return true;

            // Validar pérdida de nombramiento/servicio
            string hubNomb = hub.Nombramiento ?? "";
            string dbNomb = db.Nombramiento ?? "";
            if (!MismoTexto(hubNomb, dbNomb)) return true;

            string hubServ = hub.Servicio ?? "";
            string dbServ = db.Servicio ?? "";
            if (!MismoTexto(hubServ, dbServ)) return true;

            // Validar de forma cruzada la lista de privilegios para detectar altas O bajas
            string[] privilegiosPermitidos = { "Instructor de la Escuela del Servicio de Precursor", "Superintendente de circuito sustituto", "Miembro del CEH", "Superintendente de asamblea", "Coordinador", "Secretario", "Miembro del GVP", "Persona que pertenece a una entidad del LDC", "Superintendente auxiliar de asamblea" };

            var hubPrivs = (hub.Privilegios ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
            var dbPrivs = (db.Privilegios ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();

            foreach (var p in dbPrivs)
            {
                if (privilegiosPermitidos.Contains(p) && !hubPrivs.Contains(p)) return true;
            }
            foreach (var p in hubPrivs)
            {
                if (privilegiosPermitidos.Contains(p) && !dbPrivs.Contains(p)) return true;
            }

            if (!string.IsNullOrEmpty(hub.FechaUltimaEscuela) && !MismoTexto(db.FechaUltimaEscuela, hub.FechaUltimaEscuela)) return true;
            if (!string.IsNullOrEmpty(hub.Email) && !MismoTexto(db.Email, hub.Email)) return true;
            if (!string.IsNullOrEmpty(hub.EmailJW) && !MismoTexto(db.EmailJW, hub.EmailJW)) return true;
            if (!string.IsNullOrEmpty(hub.Telefono) && !MismoTexto(db.Telefono, hub.Telefono)) return true;
            if (!string.IsNullOrEmpty(hub.Direccion) && !MismoTexto(db.Direccion, hub.Direccion)) return true;
            if (!string.IsNullOrEmpty(hub.Foto) && db.Foto != hub.Foto) return true;

            // Si las fechas definitivas proceden de S-21/JSON, Hub no puede generar
            // conflicto ni sustituirlas. Para el resto seguimos ignorando 01/01/YYYY,
            // que es solo una aproximación calculada a partir de la edad.
            if (!db.TieneOrigenS21)
            {
                if (!string.IsNullOrEmpty(hub.FechaNacimiento) && !MismoTexto(db.FechaNacimiento, hub.FechaNacimiento) && !hub.FechaNacimiento.StartsWith("01/01/")) return true;
                if (!string.IsNullOrEmpty(hub.FechaBautismo) && !MismoTexto(db.FechaBautismo, hub.FechaBautismo) && !hub.FechaBautismo.StartsWith("01/01/")) return true;
            }

            if (!string.IsNullOrEmpty(hub.FechaInicioPrec) && !MismoTexto(db.FechaInicioPrec, hub.FechaInicioPrec) && !hub.FechaInicioPrec.StartsWith("01/01/")) return true;

            string hubSols = hub.Solicitudes ?? "";
            string dbSols = db.Solicitudes ?? "";
            var listHub = hubSols.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).OrderBy(s => s).ToList();
            var listDb = dbSols.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).OrderBy(s => s).ToList();
            if (!listHub.SequenceEqual(listDb)) return true;

            return false;
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

        private static List<IWebElement> ObtenerTarjetasPersona(IWebDriver driver)
        {
            var resultado = new List<IWebElement>();

            try
            {
                foreach (var article in driver.FindElements(By.CssSelector("article")))
                {
                    try
                    {
                        var nombre = article.FindElements(By.CssSelector("span.transliteration__vernacular")).FirstOrDefault();
                        if (nombre == null) continue;

                        string texto = (nombre.GetAttribute("textContent") ?? "").Replace("\u200B", "").Trim();
                        if (string.IsNullOrWhiteSpace(texto)) continue;

                        // Evita contar otros <article> de la página que no son personas.
                        var enlacePerfil = article.FindElements(By.XPath(".//a[(contains(@href, 'person') or contains(@href, '/records/')) or .//ptrn-translate[contains(., 'Ver más')]]")).FirstOrDefault();
                        if (enlacePerfil == null) continue;

                        resultado.Add(article);
                    }
                    catch (StaleElementReferenceException) { }
                }
            }
            catch (WebDriverException) { }

            return resultado;
        }

        private static void EsperarCambioEnListadoPersonas(IWebDriver driver, int cantidadAnterior, int segundosMaximos)
        {
            try
            {
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(segundosMaximos));
                wait.PollingInterval = TimeSpan.FromMilliseconds(250);
                wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));
                wait.Until(d => ObtenerTarjetasPersona(d).Count > cantidadAnterior);
            }
            catch { }
        }

        private static void EsperarListadoPersonasEstable(IWebDriver driver, int segundosMaximos)
        {
            DateTime limite = DateTime.UtcNow.AddSeconds(segundosMaximos);
            int cantidadAnterior = -1;
            string primerNombreAnterior = null;
            int lecturasEstables = 0;

            while (DateTime.UtcNow < limite)
            {
                try
                {
                    var tarjetas = ObtenerTarjetasPersona(driver);
                    int cantidad = tarjetas.Count;
                    string primerNombre = "";

                    if (cantidad > 0)
                    {
                        var span = tarjetas[0].FindElements(By.CssSelector("span.transliteration__vernacular")).FirstOrDefault();
                        primerNombre = (span?.GetAttribute("textContent") ?? "").Replace("\u200B", "").Trim();
                    }

                    int candidatos = 0;
                    try
                    {
                        candidatos = driver.FindElements(By.XPath("//article[.//a[contains(@href, 'person') or contains(@href, '/records/') or .//ptrn-translate[contains(., 'Ver más')]]]")).Count;
                    }
                    catch { }

                    bool todasHidratadas = candidatos == 0 || cantidad >= candidatos;

                    if (cantidad > 0 && todasHidratadas && cantidad == cantidadAnterior && string.Equals(primerNombre, primerNombreAnterior, StringComparison.Ordinal))
                    {
                        lecturasEstables++;
                        if (lecturasEstables >= 8) return; // ~2 segundos realmente estable
                    }
                    else
                    {
                        lecturasEstables = 0;
                    }

                    cantidadAnterior = cantidad;
                    primerNombreAnterior = primerNombre;
                }
                catch
                {
                    lecturasEstables = 0;
                }

                Thread.Sleep(250);
            }
        }

        private static void ExtraerSexoInformacionPersonal(IWebDriver driver, PersonaExtraida persona)
        {
            if (persona == null) return;

            try
            {
                string sexo = "";

                // Entramos expresamente en Información personal, igual que hacemos
                // con Estado, Solicitudes vigentes y Educación teocrática.
                var btnInfoPersonal = EsperaDinamicaElementoClicable(
                    driver,
                    By.XPath("//*[self::a or self::button][contains(@href, '/personal-information') or contains(@href, '/personal') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'información personal') or contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'informacion personal')]"),
                    4);

                if (btnInfoPersonal != null)
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block:'center'});", btnInfoPersonal);
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnInfoPersonal);

                    EsperaDinamicaElementoVisibilidad(
                        driver,
                        By.XPath("//dt[contains(translate(normalize-space(.), 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'sexo')]/following-sibling::dd[1]"),
                        8);

                    sexo = LeerSexoPaginaActual(driver);
                }

                // Fallback por si Hub cambia la pestaña a una vista ya activa/no clicable.
                if (string.IsNullOrWhiteSpace(sexo))
                    sexo = LeerSexoPaginaActual(driver);

                persona.Sexo = NormalizarSexoHub(sexo);
            }
            catch
            {
                // Sexo no es un dato crítico para abortar la extracción completa.
            }
        }

        private static string LeerSexoPaginaActual(IWebDriver driver)
        {
            try
            {
                var ddSexo = driver.FindElements(By.XPath("//dt[contains(translate(normalize-space(.), 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), 'sexo')]/following-sibling::dd[1]")).FirstOrDefault();
                return ddSexo == null ? "" : (ddSexo.GetAttribute("textContent") ?? "").Replace("\u200B", "").Trim();
            }
            catch { return ""; }
        }

        private static string NormalizarSexoHub(string valor)
        {
            string sexo = (valor ?? "").Trim();

            if (sexo.Equals("Masculino", StringComparison.OrdinalIgnoreCase)
                || sexo.Equals("Male", StringComparison.OrdinalIgnoreCase)
                || sexo.Equals("Hombre", StringComparison.OrdinalIgnoreCase))
                return "Hombre";

            if (sexo.Equals("Femenino", StringComparison.OrdinalIgnoreCase)
                || sexo.Equals("Female", StringComparison.OrdinalIgnoreCase)
                || sexo.Equals("Mujer", StringComparison.OrdinalIgnoreCase))
                return "Mujer";

            return "";
        }

        private static bool EsperarPaginaNombramientosValida(IWebDriver driver, int segundosMaximos)
        {
            try
            {
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(segundosMaximos));
                wait.PollingInterval = TimeSpan.FromMilliseconds(500);

                return wait.Until(d =>
                {
                    try
                    {
                        string url = d.Url ?? "";
                        if (!url.Contains("/appointments", StringComparison.OrdinalIgnoreCase)) return false;
                        if (url.Contains("/error", StringComparison.OrdinalIgnoreCase)) return false;

                        bool ready = (bool)((IJavaScriptExecutor)d).ExecuteScript("return document.readyState === 'complete';");
                        if (!ready) return false;

                        string bodyText = d.FindElement(By.TagName("body")).GetAttribute("textContent") ?? "";
                        return bodyText.IndexOf("nombr", StringComparison.OrdinalIgnoreCase) >= 0
                               || bodyText.IndexOf("recomend", StringComparison.OrdinalIgnoreCase) >= 0
                               || bodyText.IndexOf("appointment", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    catch { return false; }
                });
            }
            catch { return false; }
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

        private static IWebElement EsperaDinamicaElementoClicable(ISearchContext container, By locator, int segundosMaximos)
        {
            try
            {
                DefaultWait<ISearchContext> wait = new DefaultWait<ISearchContext>(container)
                {
                    Timeout = TimeSpan.FromSeconds(segundosMaximos),
                    PollingInterval = TimeSpan.FromMilliseconds(500)
                };
                wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));

                return wait.Until(c =>
                {
                    try
                    {
                        var elements = c.FindElements(locator);
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

        // =========================================================================
        // FUNCIONES DE CLONADO
        // =========================================================================
        private static string ClonarPerfilFirefox(string rutaOriginal, string sufijo)
        {
            string rutaClon = rutaOriginal + sufijo;
            if (Directory.Exists(rutaClon))
            {
                try { Directory.Delete(rutaClon, true); } catch { }
            }
            Directory.CreateDirectory(rutaClon);

            foreach (string dirPath in Directory.GetDirectories(rutaOriginal, "*", SearchOption.AllDirectories))
                try { Directory.CreateDirectory(dirPath.Replace(rutaOriginal, rutaClon)); } catch { }

            foreach (string newPath in Directory.GetFiles(rutaOriginal, "*.*", SearchOption.AllDirectories))
            {
                try { File.Copy(newPath, newPath.Replace(rutaOriginal, rutaClon), true); }
                catch { /* Ignorar bloqueos como parent.lock */ }
            }
            return rutaClon;
        }

        private static void EliminarPerfilClonado(string rutaClon)
        {
            try { if (Directory.Exists(rutaClon)) Directory.Delete(rutaClon, true); } catch { }
        }
    }

    public class PersonaExtraida
    {
        public string NombreCompleto { get; set; }
        // Texto exacto del nombre tal como aparece en JW Hub. Se mantiene separado
        // de NombreCompleto para no pisar el nombre consolidado de S-21/BBDD.
        public string NombreJWHub { get; set; }
        public string NombreComun { get; set; }
        public string Apellidos { get; set; }
        public string FechaInicioPrec { get; set; }
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
        public string Sexo { get; set; }
    }
}