using System.Text.Json;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

public partial class JwHubLoginPage : ContentPage
{
    private const string UrlPersonas =
        "https://hub.jw.org/congregation-persons/es/circuits/" +
        "512bc4a5-94aa-ea11-815e-0050560012f5/records";

    public JwHubLoginPage()
    {
        InitializeComponent();

        HubWebView.Source =
            UrlPersonas;
    }

    private void HubWebView_Loaded(
        object sender,
        EventArgs e)
    {
#if ANDROID
        try
        {
            var cookies =
                Android.Webkit.CookieManager.Instance;

            cookies.SetAcceptCookie(true);

            if (HubWebView.Handler?.PlatformView
                is Android.Webkit.WebView webViewAndroid)
            {
                cookies.SetAcceptThirdPartyCookies(
                    webViewAndroid,
                    true);

                webViewAndroid.Settings.JavaScriptEnabled =
                    true;

                webViewAndroid.Settings.DomStorageEnabled =
                    true;
            }
        }
        catch
        {
            // La comprobación posterior nos dirá
            // si la sesión funciona.
        }
#endif
    }

    private void HubWebView_Navigated(
        object sender,
        WebNavigatedEventArgs e)
    {
        if (e.Result ==
            WebNavigationResult.Success)
        {
#if ANDROID
            try
            {
                Android.Webkit.CookieManager
                    .Instance
                    .Flush();
            }
            catch
            {
            }
#endif

            LblEstado.Text =
                "Página cargada. Cuando hayas terminado de iniciar sesión, pulsa «Comprobar sesión».";
        }
        else
        {
            LblEstado.Text =
                $"Error cargando JW Hub: {e.Result}";
        }
    }

    private async void BtnComprobar_Click(
    object sender,
    EventArgs e)
    {
        try
        {
            LblEstado.TextColor = Colors.White;
            LblEstado.Text =
                "Comprobando la sesión...";

            // Nos aseguramos de estar en la página
            // de publicadores del circuito.
            HubWebView.Source =
                UrlPersonas;

            // Damos tiempo a JW Hub para cargar.
            await Task.Delay(3000);

            string url = "";
            string titulo = "";
            bool hayBuscar = false;
            int articulos = 0;

            // Esperamos unos segundos adicionales por si
            // Angular todavía está construyendo la página.
            for (int intento = 0;
                 intento < 10;
                 intento++)
            {
                url =
                    await EjecutarJsAsync(
                        """
                    (() => location.href || '')();
                    """);

                titulo =
                    await EjecutarJsAsync(
                        """
                    (() => document.title || '')();
                    """);

                string buscarTexto =
                    await EjecutarJsAsync(
                        """
                    (() => {
                        const normalizar = t =>
                            (t || '')
                                .replace(/\s+/g, ' ')
                                .trim()
                                .toLowerCase();

                        const botones =
                            Array.from(
                                document.querySelectorAll(
                                    'button'));

                        return botones.some(b =>
                            normalizar(b.innerText)
                                .includes('buscar'))
                            ? '1'
                            : '0';
                    })();
                    """);

                hayBuscar =
                    buscarTexto == "1";

                string articulosTexto =
                    await EjecutarJsAsync(
                        """
                    (() =>
                        document
                            .querySelectorAll('article')
                            .length
                            .toString()
                    )();
                    """);

                int.TryParse(
                    articulosTexto,
                    out articulos);

                // Si ya vemos la interfaz de Personas,
                // no seguimos esperando.
                if (hayBuscar ||
                    articulos > 0)
                {
                    break;
                }

                await Task.Delay(700);
            }

            bool enHub =
                !string.IsNullOrWhiteSpace(url)
                &&
                url.Contains(
                    "hub.jw.org",
                    StringComparison.OrdinalIgnoreCase);

            bool accesoPersonas =
                hayBuscar ||
                articulos > 0;

            if (enHub &&
                accesoPersonas)
            {
#if ANDROID
                try
                {
                    Android.Webkit.CookieManager
                        .Instance
                        .Flush();
                }
                catch
                {
                }
#endif

                JwHubSessionState
                    .MarcarSesionValida();

                LblEstado.TextColor =
                    Colors.LightGreen;

                LblEstado.Text =
                    "✓ Sesión de JW Hub guardada correctamente.";

                await DisplayAlert(
                    "JW Hub",
                    "La sesión está preparada. SCCosmos podrá reutilizarla para las sintonizaciones.",
                    "Aceptar");

                await Navigation.PopAsync();

                return;
            }

            JwHubSessionState
                .MarcarSesionNoValida();

            LblEstado.TextColor =
                Colors.Orange;

            LblEstado.Text =
                "No se ha detectado todavía acceso a la página de publicadores.\n\n" +
                $"Página: {titulo}\n" +
                $"URL: {url}\n" +
                $"Publicadores visibles: {articulos}\n\n" +
                "Si acabas de iniciar sesión, espera unos segundos y pulsa de nuevo «Comprobar sesión».";
        }
        catch (Exception ex)
        {
            LblEstado.TextColor =
                Colors.OrangeRed;

            LblEstado.Text =
                $"No se pudo comprobar la sesión:\n{ex.Message}";
        }
    }

    private async Task<string> EjecutarJsAsync(
    string javascript)
    {
        string resultado =
            await HubWebView
                .EvaluateJavaScriptAsync(
                    javascript);

        return LimpiarResultadoJavaScript(
            resultado);
    }

    private static string LimpiarResultadoJavaScript(
        string? resultado)
    {
        if (string.IsNullOrWhiteSpace(resultado))
            return "";

        string valor =
            resultado.Trim();

        // EvaluateJavaScriptAsync puede devolver:
        //
        // "texto"
        //
        // en lugar de:
        //
        // texto
        //
        // Lo intentamos decodificar como string JSON.
        try
        {
            string? decodificado =
                System.Text.Json.JsonSerializer
                    .Deserialize<string>(
                        valor);

            if (decodificado != null)
                return decodificado;
        }
        catch
        {
            // No era una cadena JSON.
        }

        // Fallback por si WebView simplemente
        // envolvió el resultado entre comillas.
        if (valor.Length >= 2 &&
            valor.StartsWith("\"") &&
            valor.EndsWith("\""))
        {
            valor =
                valor.Substring(
                    1,
                    valor.Length - 2);

            valor =
                valor
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
        }

        return valor;
    }

    private async void BtnCerrar_Click(
        object sender,
        EventArgs e)
    {
#if ANDROID
        try
        {
            Android.Webkit.CookieManager
                .Instance
                .Flush();
        }
        catch
        {
        }
#endif

        await Navigation.PopAsync();
    }

}