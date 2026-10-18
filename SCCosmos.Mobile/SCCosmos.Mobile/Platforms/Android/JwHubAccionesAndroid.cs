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
