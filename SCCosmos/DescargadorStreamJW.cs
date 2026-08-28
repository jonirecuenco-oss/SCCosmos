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
    // CLASE DE CONFIGURACIÓN (Para enlazar con los CheckBoxes de tu UI)
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

        // 3. JW.ORG (Programación Futura)
        public bool JwOrg_DiscursosBroadcasting { get; set; } = true;
        public bool JwOrg_AdoracionMatutina { get; set; } = true;
        public bool JwOrg_GraduacionGalaad { get; set; } = true;
    }

    public static class DescargadorStreamJW
    {
        public static void DescargarUltimoDiscurso(string rutaPerfilFirefox, OpcionesDescarga opciones = null)
        {
            if (opciones == null) opciones = new OpcionesDescarga();

            var service = FirefoxDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;

             Environment.SetEnvironmentVariable("MOZ_HEADLESS", "1");

            FirefoxOptions firefoxOptions = new FirefoxOptions();
            firefoxOptions.AddArgument("-profile");
            firefoxOptions.AddArgument(rutaPerfilFirefox);
            firefoxOptions.AddArgument("-headless");
            firefoxOptions.AddArgument("--window-size=1920,1080");

            string carpetaBaseDescargas = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DescargasStream");
            string carpetaTemp = Path.Combine(carpetaBaseDescargas, "_Temp");

            if (!Directory.Exists(carpetaBaseDescargas)) Directory.CreateDirectory(carpetaBaseDescargas);
            if (!Directory.Exists(carpetaTemp)) Directory.CreateDirectory(carpetaTemp);

            LimpiarCarpetaTemporal(carpetaTemp);

            firefoxOptions.SetPreference("browser.download.folderList", 2);
            firefoxOptions.SetPreference("browser.download.dir", carpetaTemp);
            firefoxOptions.SetPreference("browser.helperApps.neverAsk.saveToDisk", "video/mp4,video/webm,audio/mpeg,audio/mp3,application/octet-stream");

            using (IWebDriver driver = new FirefoxDriver(service, firefoxOptions))
            {
                // =========================================================================
                // INICIO: CONFIGURACIÓN BASE DEL NAVEGADOR
                // =========================================================================
                try
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
                    driver.Navigate().GoToUrl("https://stream.jw.org/");
                    Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error inicial conectando a JW Stream: {ex.Message}", "Fallo de conexión", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return; // Si no carga la página, abortamos todo
                }

                // Guardamos las cookies de la sesión
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

                        // Aseguramos que estamos en el inicio antes de empezar
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

                                    string carpetaDestinoFinal = Path.Combine(carpetaBaseDescargas, codigoIdioma, SanitizarParaCarpeta(categoria));

                                    foreach (var videoCard in listaVideos)
                                    {
                                        try
                                        {
                                            string nombreLimpioParaArchivo = ExtraerNombreVideo(videoCard);

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
                                                    MoverArchivoDescargado(carpetaTemp, carpetaDestinoFinal, nombreLimpioParaArchivo);
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
                        // Forzamos escape por si se ha quedado un menú abierto bloqueando la página
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

                        // Aseguramos que estamos en el inicio para no arrastrar problemas de la Fase 1
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
                                string nombreLimpioParaArchivo = ExtraerNombreVideo(videoCard);

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
                                        DescargarYConvertirHlsAsync(urlM3u8, carpetaBetelFinal, nombreLimpioParaArchivo, cookieHeader).Wait();
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

                Console.WriteLine("\n=== PROCESO COMPLETADO EXITOSAMENTE ===");
            }
        }

        // ==========================================
        // ENSAMBLADOR C# + FFMPEG (HACIA MP3)
        // ==========================================
        private static async Task DescargarYConvertirHlsAsync(string m3u8Url, string carpetaDestino, string nombreDeseado, string cookiesSession)
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

                    if (chunkLines.Count == 0) return;

                    Console.WriteLine($"   -> Ensamblando {chunkLines.Count} fragmentos temporales...");

                    using (var fs = new FileStream(rutaTsTemporal, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        for (int i = 0; i < chunkLines.Count; i++)
                        {
                            string chunkUrl = chunkLines[i];
                            if (!chunkUrl.StartsWith("http")) chunkUrl = subBasePath + chunkUrl;

                            byte[] data = await client.GetByteArrayAsync(chunkUrl);
                            await fs.WriteAsync(data, 0, data.Length);

                            if ((i + 1) % 20 == 0 || i + 1 == chunkLines.Count) Console.Write($"\r      Progreso: {i + 1}/{chunkLines.Count}...");
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
                        }
                    }
                    else
                    {
                        Console.WriteLine("   [AVISO] FFmpeg.exe no encontrado. Se conserva el archivo .ts de vídeo original.");
                        string rutaTsFinal = Path.Combine(carpetaDestino, $"{nombreDeseado}.ts");
                        File.Move(rutaTsTemporal, rutaTsFinal);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n   [ERROR] Extracción HLS: {ex.Message}");
                    try { if (File.Exists(rutaTsTemporal)) File.Delete(rutaTsTemporal); } catch { }
                }
            }
        }

        // ==========================================
        // GESTIÓN DE ARCHIVOS Y CARPETAS
        // ==========================================
        private static void MoverArchivoDescargado(string tempDir, string destinoDir, string nombreDeseado)
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
                }
                catch { }
            }
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

        private static string ExtraerNombreVideo(IWebElement videoCard)
        {
            var elemTitulo = videoCard.FindElements(By.XPath(".//h6[@data-testid='vod-program-card-title']")).FirstOrDefault();
            var elemFecha = videoCard.FindElements(By.XPath(".//p[@data-testid='vod-program-card-overline']")).FirstOrDefault();

            string tituloVideo = elemTitulo != null ? elemTitulo.GetAttribute("textContent").Trim() : "VideoDesconocido";
            string fechaVideo = elemFecha != null ? elemFecha.GetAttribute("textContent").Trim() : "";

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

        // ==========================================
        // DICCIONARIO DE IDIOMAS (WHISPER -> ISO)
        // ==========================================
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
    }
}