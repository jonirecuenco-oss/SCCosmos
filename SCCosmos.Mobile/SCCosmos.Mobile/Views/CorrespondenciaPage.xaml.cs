using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Editor de las plantillas de correspondencia (asunto y cuerpo) y de
/// sus adjuntos. Usa las mismas claves Asunto_*/Email_* y la misma
/// tabla Documentos que WPF.
/// </summary>
public partial class CorrespondenciaPage : ContentPage, IActualizableMobile
{
    private List<string> _categorias =
        new();

    private List<DocumentoVisitaMobile> _adjuntos =
        new();

    private bool _cargando;

    private bool _editorListo;

    public CorrespondenciaPage()
    {
        InitializeComponent();

        EditorCuerpo.Source =
            new HtmlWebViewSource
            {
                Html = PlantillaEditorHtml
            };
    }

    private const string PlantillaEditorHtml =
        """
        <!DOCTYPE html>
        <html>
        <head>
        <meta name="viewport"
              content="width=device-width, initial-scale=1" />
        <style>
        html, body {
            margin: 0;
            padding: 10px 12px;
            background: #101216;
            color: #FFFFFF;
            font-family: sans-serif;
            font-size: 15px;
            line-height: 1.45;
        }
        #e { min-height: 300px; outline: none; }
        a { color: #80C4FF; }
        ul { margin: 6px 0 6px 20px; padding: 0; }
        h2 { font-size: 18px; margin: 10px 0 6px 0; }
        </style>
        </head>
        <body>
        <div id="e" contenteditable="true"></div>
        <script>
        function cargar(html) {
            document.getElementById('e').innerHTML = html || '';
        }
        function ejecutar(cmd, valor) {
            document.getElementById('e').focus();
            document.execCommand(cmd, false, valor || null);
            return true;
        }
        function leer() {
            return document.getElementById('e').innerHTML || '';
        }
        </script>
        </body>
        </html>
        """;

    private void EditorCuerpo_Navigated(
        object sender,
        WebNavigatedEventArgs e)
    {
        _editorListo = true;

        _ = CargarHtmlEnEditorAsync(
            TxtCuerpo.Text ?? "");
    }

    private async Task CargarHtmlEnEditorAsync(
        string html)
    {
        if (!_editorListo)
        {
            return;
        }

        try
        {
            string literal =
                System.Text.Json.JsonSerializer.Serialize(
                    html ?? "");

            await EditorCuerpo
                .EvaluateJavaScriptAsync(
                    $"cargar({literal});");
        }
        catch
        {
        }
    }

    private async Task<string?> LeerHtmlEditorAsync()
    {
        if (!_editorListo)
        {
            return null;
        }

        try
        {
            string resultado =
                await EditorCuerpo
                    .EvaluateJavaScriptAsync(
                        "leer();");

            return DeserializarResultadoJs(
                resultado);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// En Android EvaluateJavaScriptAsync devuelve el valor entre comillas
    /// y con los caracteres escapados; aquí se deshace ese formato.
    /// </summary>
    private static string DeserializarResultadoJs(
        string? resultado)
    {
        if (string.IsNullOrWhiteSpace(resultado))
        {
            return "";
        }

        resultado =
            resultado.Trim();

        if (resultado.Length >= 2
            &&
            resultado[0] == '"'
            &&
            resultado[^1] == '"')
        {
            try
            {
                return System.Text.Json.JsonSerializer
                           .Deserialize<string>(resultado)
                       ?? "";
            }
            catch
            {
                return resultado[1..^1];
            }
        }

        if (resultado == "null")
        {
            return "";
        }

        return resultado;
    }

    private async void Formato_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                string comando)
        {
            return;
        }

        string? valor =
            null;

        if (comando == "createLink")
        {
            valor =
                await DisplayPromptAsync(
                    "Insertar enlace",
                    "Dirección (https://…)",
                    "Insertar",
                    "Cancelar",
                    "https://");

            if (string.IsNullOrWhiteSpace(
                    valor))
            {
                return;
            }

            valor =
                valor.Trim();
        }

        try
        {
            if (_editorListo)
            {
                await EditorCuerpo
                    .EvaluateJavaScriptAsync(
                        $"ejecutar('{comando}', " +
                        System.Text.Json.JsonSerializer
                            .Serialize(valor ?? "") +
                        ");");

                return;
            }

            // Sin editor listo (por ejemplo, WebView aún cargando):
            // se envuelve el texto seleccionado con la etiqueta HTML.
            string texto =
                TxtCuerpo.Text ?? "";

            TxtCuerpo.Text =
                comando switch
                {
                    "bold" => $"<b>{texto}</b>",
                    "italic" => $"<i>{texto}</i>",
                    "underline" => $"<u>{texto}</u>",
                    "h2" => $"<h2>{texto}</h2>",
                    "insertUnorderedList" =>
                        $"<ul><li>{texto}</li></ul>",
                    _ => texto
                };

            await CargarHtmlEnEditorAsync(
                TxtCuerpo.Text);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Formato",
                "No se pudo aplicar el formato: " +
                ex.Message,
                "Aceptar");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        await CargarCategoriasAsync();
        Services.Perf.Registrar(
            "Correspondencia",
            reloj.ElapsedMilliseconds);
    }

    public void DatosActualizados()
    {
        try
        {
            _ = CargarCategoriasAsync();
        }
        catch
        {
        }
    }


    private async Task CargarCategoriasAsync()
    {
        try
        {
            _categorias =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerCategoriasCorrespondenciaMobile());

            _cargando =
                true;

            PickerCategoria.ItemsSource =
                _categorias;

            if (_categorias.Count > 0)
            {
                PickerCategoria.SelectedIndex =
                    0;
            }

            _cargando =
                false;

            if (PickerCategoria.SelectedItem is
                string categoria)
            {
                await CargarCategoriaAsync(
                    categoria);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Correspondencia",
                "No se pudieron cargar las categorías: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void PickerCategoria_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        if (_cargando
            ||
            PickerCategoria.SelectedItem is not
                string categoria)
        {
            return;
        }

        await CargarCategoriaAsync(
            categoria);
    }

    private async Task CargarCategoriaAsync(
        string categoria)
    {
        try
        {
            // Las tres lecturas, juntas y fuera del hilo de interfaz.
            var datos =
                await Task.Run(() => (
                    Asunto:
                        DatabaseHelper
                            .ObtenerConfiguracionVisitaMobile(
                                "Asunto_" + categoria,
                                ""),
                    Cuerpo:
                        DatabaseHelper
                            .ObtenerConfiguracionVisitaMobile(
                                "Email_" + categoria,
                                ""),
                    Adjuntos:
                        DatabaseHelper
                            .ObtenerDocumentosVisitaMobile(
                                categoria)));

            TxtAsunto.Text =
                datos.Asunto;

            TxtCuerpo.Text =
                datos.Cuerpo;

            await CargarHtmlEnEditorAsync(
                datos.Cuerpo);

            _adjuntos =
                datos.Adjuntos;

            BindableLayout.SetItemsSource(
                ListaAdjuntos,
                _adjuntos);

            LblSinAdjuntos.IsVisible =
                _adjuntos.Count == 0;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Correspondencia",
                "No se pudo cargar la categoría: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        if (PickerCategoria.SelectedItem is not
            string categoria)
        {
            return;
        }

        try
        {
            string? htmlEditor =
                await LeerHtmlEditorAsync();

            if (htmlEditor is not null)
            {
                TxtCuerpo.Text =
                    htmlEditor;
            }

            await Task.Run(
                () =>
                {
                    DatabaseHelper.GuardarConfiguracion(
                        "Asunto_" + categoria,
                        TxtAsunto.Text?.Trim() ?? "");

                    DatabaseHelper.GuardarConfiguracion(
                        "Email_" + categoria,
                        TxtCuerpo.Text ?? "");
                });

            LblEstado.Text =
                $"Plantilla guardada: {categoria}";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Correspondencia",
                "No se pudo guardar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnAnadirAdjunto_Clicked(
        object sender,
        EventArgs e)
    {
        if (PickerCategoria.SelectedItem is not
            string categoria)
        {
            return;
        }

        try
        {
            var resultado =
                await FilePicker.Default.PickAsync(
                    new PickOptions
                    {
                        PickerTitle =
                            "Selecciona un archivo"
                    });

            if (resultado is null)
            {
                return;
            }

            using Stream stream =
                await resultado.OpenReadAsync();

            using var memoria =
                new MemoryStream();

            await stream.CopyToAsync(
                memoria);

            byte[] contenido =
                memoria.ToArray();

            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarDocumentoVisitaMobile(
                            categoria,
                            resultado.FileName,
                            "",
                            contenido));

            await CargarCategoriaAsync(
                categoria);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Correspondencia",
                "No se pudo añadir el adjunto: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEliminarAdjunto_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                DocumentoVisitaMobile documento
            ||
            PickerCategoria.SelectedItem is not
                string categoria)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar adjunto",
                $"¿Eliminar «{documento.NombreArchivo}»?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper
                        .EliminarDocumentoVisitaMobile(
                            documento.Id));

            await CargarCategoriaAsync(
                categoria);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Correspondencia",
                "No se pudo eliminar el adjunto: " +
                ex.Message,
                "Aceptar");
        }
    }
}
