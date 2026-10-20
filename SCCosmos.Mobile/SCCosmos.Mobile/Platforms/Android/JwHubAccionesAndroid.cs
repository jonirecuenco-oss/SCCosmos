#if ANDROID
using System.Globalization;

using Android.App;
using Android.Content;
using Android.Webkit;

using Microsoft.Maui.ApplicationModel;

using AWebView = Android.Webkit.WebView;

namespace SCCosmos.Mobile.Platforms.Android;


// ============================================================================
// ACCIONES SOBRE RECOMENDACIONES DE JW HUB (Android)
// ============================================================================
//
// Aprobar o cancelar una recomendación usando el mismo mecanismo que la
// extracción: un WebView con las cookies del usuario y JavaScript que
// pulsa los mismos botones que la automatización de escritorio
// ("Analizar hermano" → "Editar estado del nombramiento" →
// "Aprobar"/"Cancelar" → "Guardar" → "Enviar").
//
// IMPORTANTE: no se ha podido verificar en un dispositivo real (requiere
// cuenta JW Hub). La app no envía correos: solo cambia el estado en Hub.

public static class JwHubAccionesAndroid
{
    public sealed record ResultadoAccionHub(
        bool Correcto,
        string Mensaje);


    public static async Task<ResultadoAccionHub>
        ProcesarRecomendacionAsync(
            string urlDetalle,
            bool aprobar,
            DateTime fecha)
    {
        AWebView? webView = null;

        try
        {
            string url =
                ValidarUrl(urlDetalle);

            webView =
                await CrearWebViewAsync();

            await NavegarAsync(
                webView,
                url);

            await Task.Delay(2500);

            await EsperarYPulsarAsync(
                webView,
                "analizar hermano",
                20000,
                "Analizar hermano");

            await EsperarYPulsarAsync(
                webView,
                "editar estado del nombramiento",
                20000,
                "Editar estado del nombramiento");

            string opcion =
                aprobar
                    ? "aprobar"
                    : "cancelar";

            await EsperarYPulsarAsync(
                webView,
                opcion,
                15000,
                opcion);

            if (aprobar)
            {
                await PonerFechaAsync(
                    webView,
                    fecha);
            }

            await EsperarYPulsarAsync(
                webView,
                "guardar",
                15000,
                "Guardar");

            await Task.Delay(1200);

            await EsperarYPulsarAsync(
                webView,
                "enviar",
                20000,
                "Enviar");

            await Task.Delay(3000);

            await VerificarSinErrorAsync(
                webView);

            return new ResultadoAccionHub(
                true,
                aprobar
                    ? "Recomendación aprobada en JW Hub."
                    : "Recomendación cancelada en JW Hub.");
        }
        catch (Exception ex)
        {
            return new ResultadoAccionHub(
                false,
                ex.Message);
        }
        finally
        {
            if (webView is not null)
            {
                Destruir(webView);
            }
        }
    }


    // =========================================================
    // SINCRONIZACIÓN DE NOMBRAMIENTOS (BANDEJA DE RECOMENDACIONES)
    //
    // Espejo de la fase 2 de Windows: carga la página de nombramientos
    // de JW Hub, extrae las recomendaciones y completa el
    // «recomendado como» visitando cada detalle.
    // =========================================================

    private const string UrlNombramientos =
        "https://hub.jw.org/" +
        "congregation-persons/es/circuits/" +
        "512bc4a5-94aa-ea11-815e-0050560012f5/" +
        "appointments";


    public static async Task<
        List<SCCosmos.Mobile.Data.RecomendacionHubMobile>>
        SintonizarNombramientosAsync()
    {
        AWebView? webView =
            null;

        try
        {
            webView =
                await CrearWebViewAsync();

            await NavegarAsync(
                webView,
                UrlNombramientos);

            string html =
                "";

            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(500);

                string ruta =
                    await EvaluarJsAsync(
                        webView,
                        "location.pathname || ''");

                if (ruta.Contains(
                        "/login",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    ruta.Contains(
                        "/signin",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "La sesión de JW Hub no está iniciada.");
                }

                string estado =
                    await EvaluarJsAsync(
                        webView,
                        "document.readyState");

                if (!estado.Contains(
                        "complete",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string cuerpo =
                    await EvaluarJsAsync(
                        webView,
                        "document.body ? document.body.innerText : ''");

                if (cuerpo.Contains(
                        "nombr",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    cuerpo.Contains(
                        "recomend",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    cuerpo.Contains(
                        "appointment",
                        StringComparison.OrdinalIgnoreCase))
                {
                    html =
                        DesescaparJson(
                            await EvaluarJsAsync(
                                webView,
                                "document.documentElement.outerHTML"));

                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(
                    html))
            {
                throw new InvalidOperationException(
                    "La página de nombramientos de JW Hub no " +
                    "terminó de cargar.");
            }

            var recomendaciones =
                ExtraerRecomendacionesDelHtml(
                    html);

            // El «recomendado como» está en la página de detalle: se
            // visita cada recomendación para completarlo.
            foreach (var rec
                     in recomendaciones)
            {
                try
                {
                    await NavegarAsync(
                        webView,
                        rec.UrlDetalle);

                    await Task.Delay(1500);

                    string recComo =
                        await EvaluarJsAsync(
                            webView,
                            "(function(){var dts=document.querySelectorAll('dt');" +
                            "for(var i=0;i<dts.length;i++){var t=(dts[i].textContent||'').toLowerCase();" +
                            "if(t.indexOf('recomendaci')>=0){var dd=dts[i].nextElementSibling;" +
                            "return dd?(dd.textContent||'').trim():'';}}return '';})()");

                    rec.RecomendadoComo =
                        DesescaparJson(
                            recComo).Trim();
                }
                catch
                {
                }
            }

            return recomendaciones;
        }
        finally
        {
            if (webView is not null)
            {
                Destruir(webView);
            }
        }
    }


    private static string DesescaparJson(
        string valor)
    {
        if (string.IsNullOrWhiteSpace(
                valor)
            ||
            valor == "null")
        {
            return "";
        }

        try
        {
            string? desescapado =
                System.Text.Json.JsonSerializer
                    .Deserialize<string>(valor);

            if (desescapado is not null)
            {
                return desescapado;
            }
        }
        catch
        {
        }

        return valor;
    }


    private static List<
        SCCosmos.Mobile.Data.RecomendacionHubMobile>
        ExtraerRecomendacionesDelHtml(
            string html)
    {
        var resultados =
            new List<
                SCCosmos.Mobile.Data.RecomendacionHubMobile>();

        string[] partes =
            System.Text.RegularExpressions.Regex.Split(
                html,
                @"<h3[^>]*>.*?<ptrn-translate>(Entre visitas|Visita regular)</ptrn-translate>");

        for (int i = 1;
             i + 1 < partes.Length;
             i += 2)
        {
            string tipoVisita =
                partes[i];

            string contenidoBloque =
                partes[i + 1];

            var regexTarjeta =
                new System.Text.RegularExpressions.Regex(
                    @"<article class=""card card--clickable[^>]*>.*?</article>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match tarjeta
                     in regexTarjeta.Matches(contenidoBloque))
            {
                string tarjetaHtml =
                    tarjeta.Value;

                var enlace =
                    System.Text.RegularExpressions.Regex.Match(
                        tarjetaHtml,
                        @"<a ptrnactiontarget="""" href=""([^""]+)""[^>]*>\s*(.*?)\s*</a>");

                string url =
                    enlace.Success
                        ? enlace.Groups[1].Value
                        : "";

                string congre =
                    enlace.Success
                        ? enlace.Groups[2].Value.Trim()
                        : "";

                var fechaMatch =
                    System.Text.RegularExpressions.Regex.Match(
                        tarjetaHtml,
                        @"<dt[^>]*>.*?(?:Enviado el|Próxima visita).*?</dt>\s*<dd[^>]*>(.*?)</dd>",
                        System.Text.RegularExpressions.RegexOptions.Singleline);

                string fecha =
                    fechaMatch.Success
                        ? fechaMatch.Groups[1].Value.Trim()
                        : "";

                var hermanoMatch =
                    System.Text.RegularExpressions.Regex.Match(
                        tarjetaHtml,
                        @"<span class=""transliteration__vernacular[^>]*>(.*?)</span>");

                string hermano =
                    hermanoMatch.Success
                        ? hermanoMatch.Groups[1].Value.Trim()
                        : "";

                if (!string.IsNullOrEmpty(
                        url))
                {
                    resultados.Add(
                        new SCCosmos.Mobile.Data.RecomendacionHubMobile
                        {
                            Tipo =
                                tipoVisita,

                            Congregacion =
                                congre,

                            UrlDetalle =
                                UrlAbsoluta(url),

                            Fecha =
                                fecha,

                            Hermano =
                                hermano
                        });
                }
            }
        }

        return resultados;
    }


    private static string UrlAbsoluta(
        string ruta)
    {
        if (Uri.TryCreate(
                new Uri("https://hub.jw.org"),
                ruta,
                out Uri url))
        {
            return url.AbsoluteUri;
        }

        return ruta;
    }


    private static string ValidarUrl(
        string ruta)
    {
        if (string.IsNullOrWhiteSpace(
                ruta))
        {
            throw new InvalidOperationException(
                "La recomendación no tiene URL de detalle.");
        }

        if (!Uri.TryCreate(
                new Uri("https://hub.jw.org"),
                ruta,
                out Uri url)
            ||
            !string.Equals(
                url.Host,
                "hub.jw.org",
                StringComparison.OrdinalIgnoreCase)
            ||
            url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "La URL de la recomendación no pertenece a hub.jw.org.");
        }

        return url.AbsoluteUri;
    }


    private static Task<AWebView> CrearWebViewAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(
            () =>
            {
                Context context =
                    MainActivityActividad()
                    ?? global::Android.App.Application.Context;

                var webView =
                    new AWebView(context);

                webView.Settings.JavaScriptEnabled = true;
                webView.Settings.DomStorageEnabled = true;
                webView.Settings.DatabaseEnabled = true;
                webView.Settings.LoadsImagesAutomatically = true;

                global::Android.Webkit.CookieManager? cookies =
                    global::Android.Webkit.CookieManager.Instance;

                if (cookies is not null)
                {
                    cookies.SetAcceptCookie(true);
                    cookies.SetAcceptThirdPartyCookies(
                        webView,
                        true);

                    cookies.Flush();
                }

                return webView;
            });
    }


    private static Context? MainActivityActividad()
    {
        return Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            as Context;
    }


    private static void Destruir(
        AWebView webView)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(
                () =>
                {
                    webView.StopLoading();
                    webView.Destroy();
                });
        }
        catch
        {
        }
    }


    private static async Task NavegarAsync(
        AWebView webView,
        string url)
    {
        await MainThread.InvokeOnMainThreadAsync(
            () => webView.LoadUrl(url));

        // Esperar a que el documento esté listo.
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(500);

            string estado =
                await EvaluarJsAsync(
                    webView,
                    "document.readyState");

            if (estado.Contains(
                    "complete",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }


    private static async Task EsperarYPulsarAsync(
        AWebView webView,
        string textoNormalizado,
        int timeoutMs,
        string descripcion)
    {
        int intentos =
            Math.Max(
                1,
                timeoutMs / 500);

        for (int i = 0; i < intentos; i++)
        {
            string pulsado =
                await EvaluarJsAsync(
                    webView,
                    JavaScriptPulsarPorTexto(
                        textoNormalizado));

            if (pulsado.Contains("1"))
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException(
            $"No se encontró (o no se pudo pulsar) «{descripcion}».");
    }


    private static string JavaScriptPulsarPorTexto(
        string texto)
    {
        string escapado =
            texto
                .Replace("\\", "\\\\")
                .Replace("'", "\\'");

        return
            "(() => {" +
            "const norm = t => (t||'')" +
            ".normalize('NFD')" +
            ".replace(/[\\u0300-\\u036f]/g,'')" +
            ".replace(/\\s+/g,' ').trim().toLowerCase();" +
            "const objetivo = '" + escapado + "';" +
            "const nodos = document.querySelectorAll(" +
            "'a, button, label, [role=button], ptrn-translate, span, div');" +
            "for (const nodo of nodos) {" +
            "  if (norm(nodo.textContent).includes(objetivo)) {" +
            "    const clickable = " +
            "      nodo.closest('a, button, label, [role=button]') || nodo;" +
            "    try { clickable.scrollIntoView({block:'center'}); } catch(e) {}" +
            "    clickable.click();" +
            "    return '1';" +
            "  }" +
            "}" +
            "return '0';" +
            "})();";
    }


    private static async Task PonerFechaAsync(
        AWebView webView,
        DateTime fecha)
    {
        string valor =
            fecha.ToString(
                "d/M/yy",
                CultureInfo.InvariantCulture);

        string js =
            "(() => {" +
            "const v = '" + valor + "';" +
            "const input = " +
            "  document.querySelector(\"input#form\\\\.effectiveDate\") ||" +
            "  document.querySelector(\"input[placeholder='d/M/yy']\") ||" +
            "  document.querySelector(\"input[type='text']\");" +
            "if (!input) return '0';" +
            "input.focus();" +
            "input.value = v;" +
            "input.dispatchEvent(new Event('input', {bubbles:true}));" +
            "input.dispatchEvent(new Event('change', {bubbles:true}));" +
            "input.blur();" +
            "return '1';" +
            "})();";

        await EvaluarJsAsync(
            webView,
            js);
    }


    private static async Task VerificarSinErrorAsync(
        AWebView webView)
    {
        string ruta =
            await EvaluarJsAsync(
                webView,
                "location.pathname || ''");

        string cuerpo =
            await EvaluarJsAsync(
                webView,
                "document.body ? document.body.innerText : ''");

        if (ruta.Contains(
                "/login",
                StringComparison.OrdinalIgnoreCase)
            ||
            ruta.Contains(
                "/signin",
                StringComparison.OrdinalIgnoreCase)
            ||
            ruta.Contains(
                "/error",
                StringComparison.OrdinalIgnoreCase)
            ||
            cuerpo.Contains(
                "414 ERROR",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "JW Hub devolvió una página de error o de acceso.");
        }
    }


    private static async Task<string> EvaluarJsAsync(
        AWebView webView,
        string javascript)
    {
        var tcs =
            new TaskCompletionSource<string>();

        await MainThread.InvokeOnMainThreadAsync(
            () =>
            {
                try
                {
                    webView.EvaluateJavascript(
                        javascript,
                        new ValueCallback(
                            valor =>
                                tcs.TrySetResult(
                                    valor ?? "")));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

        return await tcs.Task;
    }


    private sealed class ValueCallback
        : Java.Lang.Object,
          IValueCallback
    {
        private readonly Action<string?> _accion;

        public ValueCallback(
            Action<string?> accion)
        {
            _accion =
                accion;
        }

        public void OnReceiveValue(
            Java.Lang.Object? value)
        {
            _accion(
                value?.ToString());
        }
    }
}
#endif
