using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace SCCosmos
{
    // =================================================================
    // CLASE DE CONFIGURACIÓN
    // =================================================================
    public class OpcionesDescarga
    {
        // 1. PROGRAMAS TEOCRÁTICOS
        public bool Teocraticos_DiscursosPublicos { get; set; } = true;
        public bool Teocraticos_AsambleaCircuitoSucursal { get; set; } = true;
        public bool Teocraticos_AsambleaCircuitoSuperintendente { get; set; } = true;
        public bool Teocraticos_AsambleaRegional { get; set; } = true;

        // 2. PROGRAMAS DE BETEL
        public bool Betel_AdoracionMatutina { get; set; } = true;

        // 3. JW.ORG
        public bool JwOrg_DiscursosBroadcasting { get; set; } = true;
        public bool JwOrg_AdoracionMatutina { get; set; } = true;
        public bool JwOrg_GraduacionGalaad { get; set; } = true;
        public bool JwOrg_ReunionAnual { get; set; } = true;
    }

    public static class DescargadorStreamJW
    {
        public static bool EstaDescargando { get; private set; }
        public static event Action<bool> DescargaCambiada;

        public static int DescargarUltimoDiscurso(string rutaPerfilFirefox, string carpetaBaseDescargas, OpcionesDescarga opciones = null)
        {
            if (EstaDescargando) return 0;

            EstaDescargando = true;
            DescargaCambiada?.Invoke(true);

            string perfilClonado = ClonarPerfilFirefox(rutaPerfilFirefox, "_TempStream");

            try
            {
                if (opciones == null) opciones = new OpcionesDescarga();

                int totalDescargados = 0;

                var service = FirefoxDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                //Environment.SetEnvironmentVariable("MOZ", "1");

                FirefoxOptions firefoxOptions = new FirefoxOptions();
                if (!string.IsNullOrEmpty(perfilClonado))
                {
                    firefoxOptions.AddArgument("-profile");
                    firefoxOptions.AddArgument(perfilClonado);
                }
                firefoxOptions.AddArgument("-headless");
                firefoxOptions.AddArgument("--window-size=1920,1080");
                firefoxOptions.AddArgument("--mute-audio");


                string carpetaTemp = Path.Combine(carpetaBaseDescargas, "_Temp");

                if (!Directory.Exists(carpetaBaseDescargas)) Directory.CreateDirectory(carpetaBaseDescargas);
                if (!Directory.Exists(carpetaTemp)) Directory.CreateDirectory(carpetaTemp);

                LimpiarCarpetaTemporal(carpetaTemp);

                firefoxOptions.SetPreference("browser.download.folderList", 2);
                firefoxOptions.SetPreference("browser.download.dir", carpetaTemp);
                firefoxOptions.SetPreference("browser.helperApps.neverAsk.saveToDisk", "video/mp4,video/webm,audio/mpeg,audio/mp3,application/octet-stream,video/3gpp");

                using (IWebDriver driver = new FirefoxDriver(service, firefoxOptions))
                {
                    try
                    {
                        driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
                        driver.Navigate().GoToUrl("https://stream.jw.org/");
                        Thread.Sleep(5000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error inicial conectando a JW Stream: {ex.Message}");
                        return 0;
                    }

                    string cookieHeader = string.Join("; ", driver.Manage().Cookies.AllCookies.Select(c => $"{c.Name}={c.Value}"));

                    // =========================================================================
                    // FASE 1: PROGRAMAS TEOCRÁTICOS
                    // =========================================================================
                    List<string> categoriasTeocraticos = new List<string>();
                    if (opciones.Teocraticos_DiscursosPublicos) categoriasTeocraticos.Add("Discursos públicos");
                    if (opciones.Teocraticos_AsambleaCircuitoSucursal) categoriasTeocraticos.Add("Asamblea de circuito con representante de la sucursal");
                    if (opciones.Teocraticos_AsambleaCircuitoSuperintendente) categoriasTeocraticos.Add("Asamblea de circuito con superintendente de circuito");
                    if (opciones.Teocraticos_AsambleaRegional) categoriasTeocraticos.Add("Asamblea regional");

                    if (categoriasTeocraticos.Count > 0)
                    {
                        try
                        {
                            Console.WriteLine("\n=== INICIANDO FASE 1: PROGRAMAS TEOCRÁTICOS ===");

                            driver.Navigate().GoToUrl("https://stream.jw.org/");
                            Thread.Sleep(3000);

                            var tabProgramas = EsperaDinamicaElementoClicable(driver, By.XPath("//button[contains(@id, 'theocraticProgram') or .//svg[@data-testid='BrdcstTheocraticProgramsIcon']]"), 15);
                            if (tabProgramas != null)
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", tabProgramas);
                                Thread.Sleep(500);
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", tabProgramas);
                                Thread.Sleep(4000);
                            }

                            var idiomasWhisper = ObtenerIdiomasWhisper();

                            foreach (string categoria in categoriasTeocraticos)
                            {
                                IWebElement tituloCategoria = BuscarTituloConScroll(driver, categoria);
                                if (tituloCategoria == null) continue;

                                var contenedorCategoria = tituloCategoria.FindElement(By.XPath("./ancestor::div[@data-testid='subcategory-row']"));

                                foreach (var kvp in idiomasWhisper)
                                {
                                    string nombreIdioma = kvp.Key;
                                    string codigoIdioma = kvp.Value;

                                    try
                                    {
                                        var btnIdioma = EsperaDinamicaElementoClicable(contenedorCategoria, By.XPath(".//div[@data-testid='language-selector']//button"), 5);
                                        if (btnIdioma != null)
                                        {
                                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', inline: 'nearest'});", btnIdioma);
                                            Thread.Sleep(500);
                                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnIdioma);
                                            Thread.Sleep(1500);

                                            var inputBuscadorIdioma = EsperaDinamicaElementoVisibilidad(driver, By.XPath("//input[@type='text' or @type='search']"), 5);
                                            if (inputBuscadorIdioma != null)
                                            {
                                                inputBuscadorIdioma.Clear();
                                                inputBuscadorIdioma.SendKeys(nombreIdioma);
                                                Thread.Sleep(1000);
                                            }

                                            var itemIdioma = EsperaDinamicaElementoClicable(driver, By.XPath($"//li[contains(., '{nombreIdioma}')] | //div[contains(@role, 'button') and contains(., '{nombreIdioma}')]"), 5);
                                            if (itemIdioma != null)
                                            {
                                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", itemIdioma);
                                                Thread.Sleep(5000);
                                            }
                                            else
                                            {
                                                new OpenQA.Selenium.Interactions.Actions(driver).SendKeys(Keys.Escape).Perform();
                                                Thread.Sleep(1000);
                                                continue;
                                            }
                                        }

                                        var tituloActualizado = EsperaDinamicaElementoVisibilidad(driver, By.XPath($"//h5[@data-testid='subcategory-heading' and contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚ', 'abcdefghijklmnopqrstuvwxyzáéíóú'), '{categoria.ToLower()}')]"), 5);
                                        if (tituloActualizado == null) continue;

                                        var contenedorActualizado = tituloActualizado.FindElement(By.XPath("./ancestor::div[@data-testid='subcategory-row']"));
                                        var listaVideos = contenedorActualizado.FindElements(By.XPath(".//div[@data-testid='vod-program-card']"));

                                        // Traducimos el nombre largo de la web a nombre corto para tu disco duro
                                        string nombreCarpeta = categoria;
                                        if (categoria == "Asamblea de circuito con representante de la sucursal") nombreCarpeta = "AC-BR";
                                        else if (categoria == "Asamblea de circuito con superintendente de circuito") nombreCarpeta = "AC-CO";

                                        string carpetaDestinoFinal = Path.Combine(carpetaBaseDescargas, codigoIdioma, SanitizarParaCarpeta(nombreCarpeta));

                                        foreach (var videoCard in listaVideos)
                                        {
                                            try
                                            {
                                                string nombreLimpioParaArchivo = ExtraerNombreVideo(videoCard, categoria);
                                                if (YaExisteArchivo(carpetaDestinoFinal, nombreLimpioParaArchivo)) continue;

                                                var btnDescargar = videoCard.FindElements(By.XPath(".//button[@data-testid='program-download-button']")).FirstOrDefault();
                                                if (btnDescargar != null)
                                                {
                                                    Console.WriteLine($"[FASE 1] Descargando: {nombreLimpioParaArchivo} ({codigoIdioma})");

                                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', inline: 'nearest'});", btnDescargar);
                                                    Thread.Sleep(800);
                                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnDescargar);
                                                    Thread.Sleep(2000);

                                                    var opcionAudio = EsperaDinamicaElementoClicable(driver, By.XPath("//li[@data-testid='program-download-option-audioOnlyp'] | //li[contains(., 'Audio')] | //button[contains(., 'Audio')]"), 5);
                                                    if (opcionAudio != null)
                                                    {
                                                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", opcionAudio);

                                                        if (MoverArchivoDescargado(carpetaTemp, carpetaDestinoFinal, nombreLimpioParaArchivo))
                                                        {
                                                            totalDescargados++;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        new OpenQA.Selenium.Interactions.Actions(driver).SendKeys(Keys.Escape).Perform();
                                                        Thread.Sleep(1000);
                                                    }
                                                }
                                            }
                                            catch (Exception) { continue; }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Fallo al procesar {nombreIdioma}: {ex.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error fatal en la FASE 1: {ex.Message}");
                            try { new OpenQA.Selenium.Interactions.Actions(driver).SendKeys(Keys.Escape).Perform(); } catch { }
                        }
                    }
                    else
                    {
                        Console.WriteLine("\n[OMITIDO] FASE 1: Programas Teocráticos (Ninguna opción marcada).");
                    }

                    // =========================================================================
                    // FASE 2: PROGRAMAS DE BETEL
                    // =========================================================================

                    if (opciones.Betel_AdoracionMatutina)
                    {
                        try
                        {
                            Console.WriteLine("\n=== INICIANDO FASE 2: PROGRAMAS DE BETEL ===");

                            driver.Navigate().GoToUrl("https://stream.jw.org/");
                            Thread.Sleep(3000);

                            var tabBetel = EsperaDinamicaElementoClicable(driver, By.XPath("//button[contains(., 'PROGRAMAS DE BETEL') or contains(., 'Programas de Betel')]"), 15);
                            if (tabBetel != null)
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", tabBetel);
                                Thread.Sleep(500);
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", tabBetel);
                                Thread.Sleep(4000);
                            }

                            string categoriaBetel = "Adoración matutina";
                            string carpetaBetelFinal = Path.Combine(carpetaBaseDescargas, "es", SanitizarParaCarpeta(categoriaBetel));

                            int cantidadVideos = 0;
                            IWebElement tituloBetelInicial = BuscarTituloConScroll(driver, categoriaBetel);
                            if (tituloBetelInicial != null)
                            {
                                var contenedorInicial = tituloBetelInicial.FindElement(By.XPath("./ancestor::div[@data-testid='subcategory-row']"));
                                cantidadVideos = contenedorInicial.FindElements(By.XPath(".//div[@data-testid='vod-program-card']")).Count;
                            }

                            for (int i = 0; i < cantidadVideos; i++)
                            {
                                try
                                {
                                    IWebElement tituloBetel = BuscarTituloConScroll(driver, categoriaBetel);
                                    if (tituloBetel == null) continue;

                                    var contenedorBetel = tituloBetel.FindElement(By.XPath("./ancestor::div[@data-testid='subcategory-row']"));
                                    var listaVideosBetel = contenedorBetel.FindElements(By.XPath(".//div[@data-testid='vod-program-card']"));

                                    if (i >= listaVideosBetel.Count) break;

                                    var videoCard = listaVideosBetel[i];
                                    string nombreLimpioParaArchivo = ExtraerNombreVideo(videoCard, categoriaBetel);

                                    if (YaExisteArchivoMp3(carpetaBetelFinal, nombreLimpioParaArchivo)) continue;

                                    var btnReproducir = videoCard.FindElements(By.XPath(".//div[@data-testid='play-icon'] | .//button[.//svg[@data-testid='PlayCircleOutlineIcon']]")).FirstOrDefault();

                                    if (btnReproducir != null)
                                    {
                                        Console.WriteLine($"\n[FASE 2] Interceptando: {nombreLimpioParaArchivo}");

                                        ((IJavaScriptExecutor)driver).ExecuteScript(@"
                                            window.jwM3u8Urls = [];
                                            if(window.jwObserver) { window.jwObserver.disconnect(); }
                                            window.jwObserver = new PerformanceObserver((list) => {
                                                list.getEntries().forEach((entry) => {
                                                    if (entry.name.includes('.m3u8')) { window.jwM3u8Urls.push(entry.name); }
                                                });
                                            });
                                            window.jwObserver.observe({ entryTypes: ['resource'] });
                                        ");

                                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', inline: 'nearest'});", btnReproducir);
                                        Thread.Sleep(500);
                                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnReproducir);

                                        Thread.Sleep(4000);

                                        string urlM3u8 = "";
                                        try
                                        {
                                            var urlsCapturadas = ((IJavaScriptExecutor)driver).ExecuteScript("return window.jwM3u8Urls;") as IEnumerable<object>;
                                            if (urlsCapturadas != null && urlsCapturadas.Any())
                                            {
                                                urlM3u8 = urlsCapturadas.Select(u => u.ToString()).FirstOrDefault();
                                            }
                                        }
                                        catch { }

                                        try
                                        {
                                            Console.WriteLine("   -> Despertando controles y cerrando vídeo...");

                                            new OpenQA.Selenium.Interactions.Actions(driver)
                                                .MoveByOffset(10, 10)
                                                .MoveByOffset(-10, -10)
                                                .Perform();

                                            Thread.Sleep(500);

                                            var btnCerrarRep = driver.FindElements(By.CssSelector("button.vjs-close-button")).FirstOrDefault(b => b.Displayed);
                                            if (btnCerrarRep != null)
                                            {
                                                btnCerrarRep.Click();
                                            }
                                            else
                                            {
                                                ((IJavaScriptExecutor)driver).ExecuteScript("var btn = document.querySelector('button.vjs-close-button'); if(btn) btn.click();");
                                            }

                                            ((IJavaScriptExecutor)driver).ExecuteScript(@"
                                                if(document.exitFullscreen) { document.exitFullscreen(); }
                                                var overlay = document.querySelector('.MuiBackdrop-root');
                                                if (overlay) { overlay.remove(); }
                                            ");
                                        }
                                        catch { }

                                        new OpenQA.Selenium.Interactions.Actions(driver).SendKeys(Keys.Escape).Perform();
                                        Thread.Sleep(1500);

                                        if (!string.IsNullOrEmpty(urlM3u8))
                                        {
                                            Console.WriteLine($"   -> Comenzando Ensamblaje HLS y Conversión a MP3...");
                                            bool exitoHls = DescargarYConvertirHlsAsync(urlM3u8, carpetaBetelFinal, nombreLimpioParaArchivo, cookieHeader).Result;
                                            if (exitoHls)
                                            {
                                                totalDescargados++;
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("   [ERROR] No se pudo interceptar el m3u8 de red.");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error en la tarjeta {i}: {ex.Message}");

                                    try
                                    {
                                        ((IJavaScriptExecutor)driver).ExecuteScript("var overlay = document.querySelector('.MuiBackdrop-root'); if (overlay) { overlay.remove(); }");
                                        new OpenQA.Selenium.Interactions.Actions(driver).SendKeys(Keys.Escape).Perform();
                                    }
                                    catch { }
                                    Thread.Sleep(1000);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error fatal en la FASE 2: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("\n[OMITIDO] FASE 2: Programas de Betel (No marcada).");
                    }

                    // =========================================================================
                    // FASE 3: JW.ORG (Página Principal)
                    // =========================================================================
                    var categoriasJw = new Dictionary<string, string>();
                    if (opciones.JwOrg_AdoracionMatutina) categoriasJw.Add("Adoración matutina", "https://www.jw.org/es/biblioteca/videos/#es/categories/VODPgmEvtMorningWorship");
                    if (opciones.JwOrg_DiscursosBroadcasting) categoriasJw.Add("Discursos JW Broadcasting", "https://www.jw.org/es/biblioteca/videos/#es/categories/StudioTalks");
                    if (opciones.JwOrg_GraduacionGalaad) categoriasJw.Add("Graduación de Galaad", "https://www.jw.org/es/biblioteca/videos/#es/categories/VODPgmEvtGilead");
                    if (opciones.JwOrg_ReunionAnual) categoriasJw.Add("Reunión anual", "https://www.jw.org/es/biblioteca/videos/#es/categories/VODPgmEvtAnnMtg");

                    if (categoriasJw.Count > 0)
                    {
                        try
                        {
                            Console.WriteLine("\n=== INICIANDO FASE 3: JW.ORG ===");

                            foreach (var cat in categoriasJw)
                            {
                                string nombreCat = cat.Key;
                                string urlCat = cat.Value;
                                string carpetaDestino = Path.Combine(carpetaBaseDescargas, "es", SanitizarParaCarpeta("JW " + nombreCat));

                                if (!Directory.Exists(carpetaDestino)) Directory.CreateDirectory(carpetaDestino);

                                driver.Navigate().GoToUrl(urlCat);
                                Thread.Sleep(4000); // Tiempo de carga de la web

                                // Hacemos un poco de scroll hacia abajo por si hay lazy-load
                                ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                                Thread.Sleep(2000);

                                var enlaces = driver.FindElements(By.XPath("//div[contains(@class, 'syn-body')]//h3/a"));
                                var listaVideosJw = new List<Tuple<string, string>>();

                                foreach (var enlace in enlaces)
                                {
                                    string titulo = enlace.GetAttribute("textContent").Trim();
                                    string href = enlace.GetAttribute("href");

                                    if (!string.IsNullOrEmpty(titulo) && !string.IsNullOrEmpty(href))
                                    {
                                        // EXCLUIR VÍDEOS QUE SEAN EL PROGRAMA COMPLETO DE BROADCASTING
                                        if (titulo.IndexOf("JW Broadcasting", StringComparison.OrdinalIgnoreCase) >= 0)
                                            continue;

                                        listaVideosJw.Add(new Tuple<string, string>(titulo, href));
                                    }
                                }

                                Console.WriteLine($"\n-> Explorando '{nombreCat}': {listaVideosJw.Count} videos aplicables encontrados.");

                                foreach (var video in listaVideosJw)
                                {
                                    string tituloLimpio = SanitizarParaCarpeta(video.Item1);

                                    if (YaExisteArchivo(carpetaDestino, tituloLimpio)) continue;

                                    Console.WriteLine($"\n[FASE 3] Procesando: {tituloLimpio}");
                                    driver.Navigate().GoToUrl(video.Item2);
                                    Thread.Sleep(3000);

                                    // Quitamos el banner de las cookies de JW.org que suele bloquear el clic de descarga
                                    try { ((IJavaScriptExecutor)driver).ExecuteScript("var banner = document.getElementById('dir_cookieBanner'); if(banner) banner.remove();"); } catch { }

                                    var btnDescargar = EsperaDinamicaElementoClicable(driver, By.XPath("//div[contains(@class, 'downloadDropdown')]//button[contains(@class, 'dropdownHandle')]"), 10);
                                    if (btnDescargar != null)
                                    {
                                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btnDescargar);
                                        Thread.Sleep(500);
                                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnDescargar);
                                        Thread.Sleep(1500);

                                        // Buscamos específicamente la resolución 144p
                                        var opcion144p = driver.FindElements(By.XPath("//div[contains(@class, 'dropdownBody')]//a[contains(@class, 'secondaryButton') and contains(., '144p')]")).FirstOrDefault();

                                        if (opcion144p != null)
                                        {
                                            string fileUrl = opcion144p.GetAttribute("href");

                                            if (!string.IsNullOrEmpty(fileUrl))
                                            {
                                                Console.WriteLine($"   -> Encontrada opción 144p. Descargando de red: {fileUrl}");

                                                // Descargador hiper-rápido por HttpClient directo
                                                bool exito = DescargarArchivoDirecto(fileUrl, carpetaDestino, tituloLimpio).Result;
                                                if (exito) totalDescargados++;
                                            }
                                        }
                                        else
                                        {
                                            // Fallback: Si no hay 144p, buscamos todas las resoluciones y cogemos la más baja
                                            var opcionesDescarga = driver.FindElements(By.XPath("//div[contains(@class, 'dropdownBody')]//a[contains(@class, 'secondaryButton')]"));
                                            if (opcionesDescarga.Count > 0)
                                            {
                                                var linkMasBajo = opcionesDescarga.Last(); // La más baja suele ser la última
                                                string fileUrl = linkMasBajo.GetAttribute("href");

                                                if (!string.IsNullOrEmpty(fileUrl))
                                                {
                                                    Console.WriteLine($"   -> [AVISO] 144p no encontrado. Descargando la resolución más baja disponible: {fileUrl}");
                                                    bool exito = DescargarArchivoDirecto(fileUrl, carpetaDestino, tituloLimpio).Result;
                                                    if (exito) totalDescargados++;
                                                }
                                            }
                                            else
                                            {
                                                Console.WriteLine("   [AVISO] No se encontraron links de descarga dentro del desplegable.");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("   [AVISO] Botón de descarga general no encontrado en la página.");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error fatal en la FASE 3 (JW.ORG): {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("\n[OMITIDO] FASE 3: JW.ORG (Ninguna opción marcada).");
                    }


                    Console.WriteLine("\n=== PROCESO COMPLETADO EXITOSAMENTE ===");
                    return totalDescargados;
                }
            }
            finally
            {
                EstaDescargando = false;
                DescargaCambiada?.Invoke(false);
                EliminarPerfilClonado(perfilClonado);
            }
        }

        // ==========================================
        // DESCARGADOR RÁPIDO PARA JW.ORG (Sin FFmpeg)
        // ==========================================
        private static async Task<bool> DescargarArchivoDirecto(string url, string carpetaDestino, string nombreBase)
        {
            try
            {
                using (var handler = new HttpClientHandler())
                using (var client = new HttpClient(handler))
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        string extension = Path.GetExtension(new Uri(url).AbsolutePath);
                        if (string.IsNullOrEmpty(extension)) extension = ".mp4";

                        string rutaFinal = Path.Combine(carpetaDestino, $"{nombreBase}{extension}");
                        if (File.Exists(rutaFinal))
                        {
                            rutaFinal = Path.Combine(carpetaDestino, $"{nombreBase}_{DateTime.Now.Ticks.ToString().Substring(12)}{extension}");
                        }

                        using (var fs = new FileStream(rutaFinal, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            await stream.CopyToAsync(fs);
                        }
                        Console.WriteLine($"   [OK] Archivo guardado directo en: {rutaFinal}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   [ERROR] Ocurrió un error en la descarga directa: {ex.Message}");
                return false;
            }
        }

        // ==========================================
        // ENSAMBLADOR C# + FFMPEG (HACIA MP3) PARA STREAM
        // ==========================================
        private static async Task<bool> DescargarYConvertirHlsAsync(string m3u8Url, string carpetaDestino, string nombreDeseado, string cookiesSession)
        {
            string rutaTsTemporal = Path.Combine(carpetaDestino, $"{nombreDeseado}_temp.ts");
            string rutaMp3Final = Path.Combine(carpetaDestino, $"{nombreDeseado}.mp3");

            if (File.Exists(rutaMp3Final)) rutaMp3Final = Path.Combine(carpetaDestino, $"{nombreDeseado}_{DateTime.Now.Ticks.ToString().Substring(12)}.mp3");

            using (var handler = new HttpClientHandler())
            using (var client = new HttpClient(handler))
            {
                client.DefaultRequestHeaders.Add("Cookie", cookiesSession);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                try
                {
                    if (!Directory.Exists(carpetaDestino)) Directory.CreateDirectory(carpetaDestino);

                    string masterContent = await client.GetStringAsync(m3u8Url);
                    string basePath = m3u8Url.Substring(0, m3u8Url.LastIndexOf('/') + 1);
                    string playlistAudioOVideoligero = m3u8Url;

                    if (masterContent.Contains("#EXT-X-STREAM-INF"))
                    {
                        var lineas = masterContent.Split('\n').Select(l => l.Trim()).ToList();
                        int resolucionMinima = int.MaxValue;

                        for (int i = 0; i < lineas.Count; i++)
                        {
                            if (lineas[i].StartsWith("#EXT-X-STREAM-INF"))
                            {
                                int resolucionActual = 9999;

                                if (lineas[i].Contains("RESOLUTION="))
                                {
                                    string resStr = lineas[i].Split(new string[] { "RESOLUTION=" }, StringSplitOptions.None)[1].Split(',')[0].Split('x')[1];
                                    int.TryParse(resStr, out resolucionActual);
                                }
                                else if (lineas[i].Contains("BANDWIDTH="))
                                {
                                    string bwStr = lineas[i].Split(new string[] { "BANDWIDTH=" }, StringSplitOptions.None)[1].Split(',')[0];
                                    int.TryParse(bwStr, out resolucionActual);
                                }

                                if (resolucionActual < resolucionMinima && i + 1 < lineas.Count && !lineas[i + 1].StartsWith("#"))
                                {
                                    resolucionMinima = resolucionActual;
                                    playlistAudioOVideoligero = lineas[i + 1];
                                    if (!playlistAudioOVideoligero.StartsWith("http")) playlistAudioOVideoligero = basePath + playlistAudioOVideoligero;
                                }
                            }
                        }
                    }

                    string subContent = await client.GetStringAsync(playlistAudioOVideoligero);
                    string subBasePath = playlistAudioOVideoligero.Substring(0, playlistAudioOVideoligero.LastIndexOf('/') + 1);

                    var chunkLines = subContent.Split('\n').Select(l => l.Trim()).Where(l => !l.StartsWith("#") && !string.IsNullOrEmpty(l)).ToList();

                    if (chunkLines.Count == 0) return false;

                    Console.WriteLine($"   -> Ensamblando {chunkLines.Count} fragmentos temporales...");

                    using (var fs = new FileStream(rutaTsTemporal, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        for (int i = 0; i < chunkLines.Count; i++)
                        {
                            string chunkUrl = chunkLines[i];
                            if (!chunkUrl.StartsWith("http")) chunkUrl = subBasePath + chunkUrl;

                            byte[] data = await client.GetByteArrayAsync(chunkUrl);
                            await fs.WriteAsync(data, 0, data.Length);

                            if ((i + 1) % 20 == 0 || i + 1 == chunkLines.Count) Console.Write($"\r     Progreso: {i + 1}/{chunkLines.Count}...");
                        }
                    }

                    Console.WriteLine("\n   -> Extrayendo audio MP3 (eliminando vídeo con FFmpeg)...");

                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

                    if (File.Exists(ffmpegPath) && File.Exists(rutaTsTemporal))
                    {
                        var process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = ffmpegPath;

                        process.StartInfo.Arguments = $"-i \"{rutaTsTemporal}\" -vn -c:a libmp3lame -q:a 4 -y \"{rutaMp3Final}\"";

                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        process.Start();

                        await Task.Run(() => process.WaitForExit());

                        if (File.Exists(rutaMp3Final) && new FileInfo(rutaMp3Final).Length > 0)
                        {
                            File.Delete(rutaTsTemporal);
                            long mb = new FileInfo(rutaMp3Final).Length / 1024 / 1024;
                            Console.WriteLine($"   [OK] ¡Guardado! MP3 Puro. Tamaño: {mb} MB.");
                            return true;
                        }
                    }
                    else
                    {
                        Console.WriteLine("   [AVISO] FFmpeg.exe no encontrado. Se conserva el archivo .ts de vídeo original.");
                        string rutaTsFinal = Path.Combine(carpetaDestino, $"{nombreDeseado}.ts");
                        File.Move(rutaTsTemporal, rutaTsFinal);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n   [ERROR] Extracción HLS: {ex.Message}");
                    try { if (File.Exists(rutaTsTemporal)) File.Delete(rutaTsTemporal); } catch { }
                    return false;
                }
            }
            return false;
        }

        // ==========================================
        // GESTIÓN DE ARCHIVOS Y CARPETAS
        // ==========================================
        private static bool MoverArchivoDescargado(string tempDir, string destinoDir, string nombreDeseado)
        {
            if (!Directory.Exists(destinoDir)) Directory.CreateDirectory(destinoDir);

            DateTime inicioEspera = DateTime.Now;
            FileInfo archivoNuevo = null;

            Thread.Sleep(2000);

            while ((DateTime.Now - inicioEspera).TotalSeconds < 60)
            {
                var files = new DirectoryInfo(tempDir).GetFiles();

                if (files.Length == 0) { Thread.Sleep(1000); continue; }
                if (files.Any(f => f.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase))) { Thread.Sleep(1000); continue; }

                archivoNuevo = files.OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (archivoNuevo != null && archivoNuevo.Length > 0) break;

                Thread.Sleep(1000);
            }

            if (archivoNuevo != null)
            {
                try
                {
                    string ext = archivoNuevo.Extension;
                    string rutaFinal = Path.Combine(destinoDir, $"{nombreDeseado}{ext}");

                    if (File.Exists(rutaFinal))
                        rutaFinal = Path.Combine(destinoDir, $"{nombreDeseado}_{DateTime.Now.Ticks.ToString().Substring(12)}{ext}");

                    archivoNuevo.MoveTo(rutaFinal);
                    Console.WriteLine($"   [OK] Archivo guardado: {rutaFinal}");
                    return true;
                }
                catch { return false; }
            }
            return false;
        }

        private static void LimpiarCarpetaTemporal(string ruta)
        {
            try
            {
                var di = new DirectoryInfo(ruta);
                foreach (FileInfo file in di.GetFiles()) { try { file.Delete(); } catch { } }
            }
            catch { }
        }

        private static bool YaExisteArchivo(string carpetaDestino, string nombreLimpioParaArchivo)
        {
            if (Directory.Exists(carpetaDestino))
            {
                var archivosExistentes = new DirectoryInfo(carpetaDestino).GetFiles();
                if (archivosExistentes.Any(f => f.Name.StartsWith(nombreLimpioParaArchivo, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[OMITIDO] Ya existe: {nombreLimpioParaArchivo}");
                    return true;
                }
            }
            return false;
        }

        private static bool YaExisteArchivoMp3(string carpetaDestino, string nombreLimpioParaArchivo)
        {
            if (Directory.Exists(carpetaDestino))
            {
                var archivosExistentes = new DirectoryInfo(carpetaDestino).GetFiles("*.mp3");
                if (archivosExistentes.Any(f => f.Name.StartsWith(nombreLimpioParaArchivo, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[OMITIDO] Ya existe: {nombreLimpioParaArchivo}");
                    return true;
                }
            }
            return false;
        }

        private static string SanitizarParaCarpeta(string nombreOriginal)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) nombreOriginal = nombreOriginal.Replace(c, '_');
            return nombreOriginal.Trim();
        }

        private static string ExtraerNombreVideo(IWebElement videoCard, string categoria = "")
        {
            var elemTitulo = videoCard.FindElements(By.XPath(".//h6[@data-testid='vod-program-card-title']")).FirstOrDefault();
            var elemFecha = videoCard.FindElements(By.XPath(".//p[@data-testid='vod-program-card-overline']")).FirstOrDefault();

            string tituloVideo = elemTitulo != null ? elemTitulo.GetAttribute("textContent").Trim() : "VideoDesconocido";
            string fechaVideo = elemFecha != null ? elemFecha.GetAttribute("textContent").Trim() : "";

            if (categoria.Equals("Adoración matutina", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(fechaVideo))
                {
                    string fechaNumerica = ConvertirFechaJWHub(fechaVideo);
                    if (!string.IsNullOrEmpty(fechaNumerica) && fechaNumerica.Contains("/"))
                    {
                        string[] partes = fechaNumerica.Split('/');
                        if (partes.Length == 3)
                        {
                            return $"{partes[2]}-{partes[1]}-{partes[0]} - {tituloVideo}";
                        }
                    }
                }
            }

            string nombreCombinado = string.IsNullOrWhiteSpace(fechaVideo) ? tituloVideo : $"{fechaVideo} - {tituloVideo}";

            string nombreLimpio = nombreCombinado.Replace("/", "-").Replace("\\", "-").Replace(":", " -").Replace("?", "");
            foreach (char c in Path.GetInvalidFileNameChars()) nombreLimpio = nombreLimpio.Replace(c, '_');

            return nombreLimpio.Trim();
        }

        // ==========================================
        // UTILIDADES DE NAVEGACIÓN Y BÚSQUEDA
        // ==========================================
        private static IWebElement BuscarTituloConScroll(IWebDriver driver, string categoria)
        {
            IWebElement tituloCategoria = null;
            string busquedaFlexible = categoria.ToLower();

            for (int scrollTry = 0; scrollTry < 10; scrollTry++)
            {
                var todosLosTitulos = driver.FindElements(By.XPath("//h5[@data-testid='subcategory-heading']"));
                tituloCategoria = todosLosTitulos.FirstOrDefault(t => t.GetAttribute("textContent").ToLower().Contains(busquedaFlexible));
                if (tituloCategoria != null && tituloCategoria.Displayed) break;

                ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollBy(0, 800);");
                Thread.Sleep(1000);
            }

            if (tituloCategoria != null)
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', inline: 'nearest'});", tituloCategoria);
                Thread.Sleep(1500);
            }
            return tituloCategoria;
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

        private static Dictionary<string, string> ObtenerIdiomasWhisper()
        {
            return new Dictionary<string, string>
            {
                { "Español (España)", "es" }, { "Español (Argentina)", "es" }, { "Español (México)", "es" },
                { "Inglés (Estados Unidos)", "en" }, { "Catalán (España)", "ca" }, { "Valenciano (España)", "ca" },
                { "Gallego (España)", "gl" }, { "Euskera (España)", "eu" }, { "Alemán (Alemania)", "de" },
                { "Francés (Francia)", "fr" }, { "Francés (Bélgica)", "fr" }, { "Italiano (Italia)", "it" },
                { "Portugués de Portugal", "pt" }, { "Portugués (Brasil)", "pt" }, { "Ruso", "ru" },
                { "Ucraniano (Ucrania)", "uk" }, { "Griego (Grecia)", "el" }, { "Búlgaro (Bulgaria)", "bg" },
                { "Checo (Checa, República)", "cs" }, { "Croata (Croacia)", "hr" }, { "Eslovaco (Eslovaquia)", "sk" },
                { "Esloveno (Eslovenia)", "sl" }, { "Albanés (Albania)", "sq" }, { "Amárico (Etiopía)", "am" },
                { "Árabe", "ar" }, { "Armenio (Armenia)", "hy" }, { "Asamés (India)", "as" },
                { "Azerí (Georgia)", "az" }, { "Bashkir", "ba" }, { "Bengalí (India)", "bn" },
                { "Birmano (Myanmar)", "my" }, { "Camboyano (Japón)", "km" },
                { "Chino cantonés (tradicional) (Hong Kong)", "yue" }, { "Chino mandarín (simplificado) (Taiwán)", "zh" },
                { "Chino mandarín (tradicional) (Taiwán)", "zh" }, { "Cingalés (Sri Lanka)", "si" },
                { "Coreano (Corea del Sur)", "ko" }, { "Danés (Dinamarca)", "da" }, { "Feroés (Feroe (islas))", "fo" },
                { "Finlandés (Finlandia)", "fi" }, { "Galés (Reino Unido)", "cy" }, { "Georgiano (Georgia)", "ka" },
                { "Gujaratí (India)", "gu" }, { "Hausa (Nigeria)", "ha" }, { "Hebreo (Israel)", "he" },
                { "Hindi (India)", "hi" }, { "Húngaro (Hungría)", "hu" }, { "Indonesio (Indonesia)", "id" },
                { "Islandés (Islandia)", "is" }, { "Japonés (Japón)", "ja" }, { "Kannada (India)", "kn" },
                { "Kazajo (Kazajistán)", "kk" }, { "Lao (Tailandia)", "lo" }, { "Lingala (Congo, República Democrática del)", "ln" },
                { "Macedonio (Macedonia del Norte, República de)", "mk" }, { "Malayálam (India)", "ml" },
                { "Malayo (Malasia)", "ms" }, { "Malgache (Madagascar)", "mg" }, { "Maltés (Reino Unido)", "mt" },
                { "Maratí (India)", "mr" }, { "Mongol (Japón)", "mn" }, { "Nepalés (Nepal)", "ne" },
                { "Noruego (Noruega)", "no" }, { "Persa (Alemania)", "fa" }, { "Polaco (Polonia)", "pl" },
                { "Punyabí (India)", "pa" }, { "Rumano (Rumania)", "ro" }, { "Serbian (Serbia)", "sr" },
                { "Shona (Zimbabue)", "sn" }, { "Suajili (Tanzania)", "sw" }, { "Sueco (Suecia)", "sv" },
                { "Tagalo (Filipinas)", "tl" }, { "Tai (Tailandia)", "th" }, { "Tamil (India)", "ta" },
                { "Tártaro", "tt" }, { "Tayiko (Kazajistán)", "tg" }, { "Telugu (India)", "te" },
                { "Turco (Turquía)", "tr" }, { "Urdu (Reino Unido)", "ur" }, { "Uzbeko (Kazajistán)", "uz" },
                { "Vietnamita", "vi" }, { "Yoruba (Nigeria)", "yo" }
            };
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
                catch { }
            }
            return rutaClon;
        }

        private static void EliminarPerfilClonado(string rutaClon)
        {
            try { if (Directory.Exists(rutaClon)) Directory.Delete(rutaClon, true); } catch { }
        }
    }
}