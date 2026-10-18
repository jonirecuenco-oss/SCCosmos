using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Detalle de una asamblea: programa y organigrama (solo lectura).
/// </summary>
public partial class AsambleaDetallePage : ContentPage
{
    private readonly AsambleaMobile _asamblea;

    private List<ParteAsambleaMobile> _partes =
        new();

    private List<OrganigramaAsambleaMobile> _organigrama =
        new();

    public AsambleaDetallePage(
        AsambleaMobile asamblea)
    {
        InitializeComponent();

        _asamblea =
            asamblea;

        Title =
            asamblea.TituloCabecera;

        LblTitulo.Text =
            asamblea.TituloCabecera;

        LblDetalle.Text =
            asamblea.Detalle;

        LblTematico.Text =
            asamblea.TextoTematico;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CargarAsync();
    }

    private async void BtnAnadirParte_Clicked(
        object sender,
        EventArgs e)
    {
        await AbrirEditorParteAsync(
            null);
    }

    private async void BtnPdfPrograma_Clicked(
        object sender,
        EventArgs e)
    {
        await GenerarYShareAsync(
            () =>
                AsambleaPdfService.GenerarPrograma(
                    _asamblea,
                    _partes),
            $"Programa {_asamblea.Seccion}",
            "programa");
    }

    private async void BtnPdfOrganigrama_Clicked(
        object sender,
        EventArgs e)
    {
        await GenerarYShareAsync(
            () =>
                AsambleaPdfService.GenerarOrganigrama(
                    _asamblea,
                    _organigrama),
            $"Organigrama {_asamblea.Seccion}",
            "organigrama");
    }

    private async void BtnEnviarAsignacion_Clicked(
        object sender,
        EventArgs e)
    {
        const string categoria =
            "Asignación asamblea circuito";

        string asunto =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Asunto_" + categoria,
                    "Asignación: asamblea de circuito");

        string cuerpo =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Email_" + categoria,
                    "Querido hermano, adjuntamos tu asignación " +
                    "para la próxima asamblea de circuito.");

        List<string> destinatarios =
            CorreoService.CorreosDePersonas(
                _partes.Select(p => p.AsignadoId));

        if (destinatarios.Count == 0)
        {
            await DisplayAlert(
                "Enviar asignación",
                "No hay correos disponibles de los hermanos " +
                "asignados. Añade el email en su ficha.",
                "Aceptar");

            return;
        }

        await CorreoService.ComponerAsync(
            asunto,
            cuerpo,
            destinatarios,
            CorreoService.MaterializarCategoria(
                categoria));
    }

    private async void BtnEnviarOrganigrama_Clicked(
        object sender,
        EventArgs e)
    {
        const string categoria =
            "Envío de organigrama";

        string asunto =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Asunto_" + categoria,
                    "Organigrama de la asamblea de circuito");

        string cuerpo =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Email_" + categoria,
                    "Queridos hermanos, adjuntamos el organigrama " +
                    "de la próxima asamblea de circuito.");

        List<string> destinatarios =
            CorreoService.CorreosDePersonas(
                _organigrama.Select(o => o.AsignadoId));

        var adjuntos =
            new List<string>();

        try
        {
            byte[] pdf =
                await Task.Run(
                    () =>
                        AsambleaPdfService
                            .GenerarOrganigrama(
                                _asamblea,
                                _organigrama));

            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    $"Organigrama_{_asamblea.Id}.pdf");

            await File.WriteAllBytesAsync(
                ruta,
                pdf);

            adjuntos.Add(
                ruta);
        }
        catch
        {
            // Sin PDF si falla la generación.
        }

        adjuntos.AddRange(
            CorreoService.MaterializarCategoria(
                categoria));

        if (destinatarios.Count == 0
            &&
            adjuntos.Count == 0)
        {
            await DisplayAlert(
                "Enviar organigrama",
                "No hay correos de los asignados ni documentos " +
                "en «Envío de organigrama».",
                "Aceptar");

            return;
        }

        await CorreoService.ComponerAsync(
            asunto,
            cuerpo,
            destinatarios,
            adjuntos);
    }

    private async Task GenerarYShareAsync(
        Func<byte[]> generador,
        string titulo,
        string sufijo)
    {
        try
        {
            byte[] pdf =
                await Task.Run(
                    generador);

            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    $"Asamblea_{_asamblea.Id}_{sufijo}.pdf");

            await File.WriteAllBytesAsync(
                ruta,
                pdf);

            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title = titulo,
                    File = new ShareFile(ruta)
                });
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "PDF",
                "No se pudo generar el PDF: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEditarParte_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                ParteAsambleaMobile parte)
        {
            await AbrirEditorParteAsync(
                parte);
        }
    }

    private async void BtnEliminarParte_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                ParteAsambleaMobile parte)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar parte",
                $"¿Eliminar «{parte.Titulo}»?",
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
                        .EliminarParteAsambleaMobile(
                            parte.Id));

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Parte",
                "No se pudo eliminar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async Task AbrirEditorParteAsync(
        ParteAsambleaMobile? parte)
    {
        var editor =
            new ParteAsambleaEdicionPage(
                _asamblea.Id,
                parte);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
    }


    private async void BtnAnadirOrganigrama_Clicked(
        object sender,
        EventArgs e)
    {
        await AbrirEditorOrganigramaAsync(
            null);
    }

    private async void BtnEditarOrganigrama_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                OrganigramaAsambleaMobile fila)
        {
            await AbrirEditorOrganigramaAsync(
                fila);
        }
    }

    private async void BtnEliminarOrganigrama_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                OrganigramaAsambleaMobile fila)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar asignación",
                $"¿Eliminar «{fila.Departamento} · {fila.Rol}»?",
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
                        .EliminarOrganigramaAsambleaMobile(
                            fila.Id));

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Organigrama",
                "No se pudo eliminar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async Task AbrirEditorOrganigramaAsync(
        OrganigramaAsambleaMobile? fila)
    {
        var editor =
            new OrganigramaEdicionPage(
                _asamblea.Id,
                fila);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
    }


    private async Task CargarAsync()
    {
        try
        {
            var resultado =
                await Task.Run(
                    () =>
                    {
                        List<ParteAsambleaMobile> partes =
                            DatabaseHelper
                                .ObtenerPartesAsambleaMobile(
                                    _asamblea.Id);

                        List<OrganigramaAsambleaMobile>
                            organigrama =
                                DatabaseHelper
                                    .ObtenerOrganigramaAsambleaMobile(
                                        _asamblea.Id);

                        return (partes, organigrama);
                    });

            _partes =
                resultado.partes;

            _organigrama =
                resultado.organigrama;

            BindableLayout.SetItemsSource(
                ListaPartes,
                resultado.partes);

            LblSinPartes.IsVisible =
                resultado.partes.Count == 0;

            BindableLayout.SetItemsSource(
                ListaOrganigrama,
                resultado.organigrama);

            LblSinOrganigrama.IsVisible =
                resultado.organigrama.Count == 0;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Asamblea",
                "No se pudo cargar el detalle: " +
                ex.Message,
                "Aceptar");
        }
    }
}
