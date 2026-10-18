using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Selector de estudiantes para una clase: precursores y misioneros
/// (se excluyen los que ya están apuntados), con búsqueda y casillas.
/// </summary>
public partial class EscuelaAlumnosPickerPage : ContentPage
{
    private readonly string _escuelaId;

    private List<AlumnoPosibleEscuelaMobile> _posibles =
        new();

    public EscuelaAlumnosPickerPage(
        string escuelaId)
    {
        InitializeComponent();

        _escuelaId =
            escuelaId ?? "";

        _ = CargarAsync();
    }


    private async Task CargarAsync()
    {
        try
        {
            var resultado =
                await Task.Run(
                    () =>
                    {
                        List<EscuelaAlumnoMobile> yaApuntados =
                            DatabaseHelper
                                .ObtenerAlumnosEscuelaMobile(
                                    _escuelaId);

                        List<Persona> personas =
                            DatabaseHelper
                                .ObtenerPersonas();

                        return (yaApuntados, personas);
                    });

            var idsApuntados =
                resultado.yaApuntados
                    .Select(
                        a => a.PersonaId)
                    .ToHashSet();

            _posibles =
                resultado.personas
                    .Where(
                        p =>
                            !idsApuntados.Contains(
                                p.Id)
                            && (p.Servicio ?? "").Length > 0
                            && ((p.Servicio ?? "").IndexOf(
                                    "Precursor regular",
                                    StringComparison.OrdinalIgnoreCase) >= 0
                                || (p.Servicio ?? "").IndexOf(
                                    "Precursor especial",
                                    StringComparison.OrdinalIgnoreCase) >= 0
                                || (p.Servicio ?? "").IndexOf(
                                    "Misionero",
                                    StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(
                        p => new AlumnoPosibleEscuelaMobile
                        {
                            Persona =
                                p,
                            Nombre =
                                string.IsNullOrWhiteSpace(
                                    p.NombreComun)
                                    ? p.Nombre
                                    : p.NombreComun,
                            Detalle =
                                string.IsNullOrWhiteSpace(
                                    p.CongregacionNombre)
                                    ? p.CongregacionCodigo
                                    : p.CongregacionNombre
                        })
                    .OrderBy(
                        a => a.Nombre)
                    .ToList();

            AplicarFiltro();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Estudiantes",
                "No se pudieron cargar los posibles estudiantes:\n"
                + ex.Message,
                "Aceptar");
        }
    }


    private void AplicarFiltro()
    {
        string busqueda =
            (TxtBuscar.Text ?? "").Trim();

        ListaPosibles.ItemsSource =
            string.IsNullOrWhiteSpace(
                busqueda)
                ? _posibles
                : _posibles
                    .Where(
                        a =>
                            a.Nombre.IndexOf(
                                busqueda,
                                StringComparison.OrdinalIgnoreCase) >= 0
                            || (a.Detalle ?? "").IndexOf(
                                busqueda,
                                StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
    }


    private void TxtBuscar_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        AplicarFiltro();
    }


    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }


    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        var seleccionados =
            _posibles
                .Where(
                    a => a.Seleccionado)
                .Select(
                    a => a.Persona)
                .ToList();

        if (seleccionados.Count == 0)
        {
            await DisplayAlert(
                "Añadir estudiantes",
                "Marca al menos un estudiante para añadirlo.",
                "Aceptar");

            return;
        }

        try
        {
            int agregados =
                await Task.Run(
                    () => DatabaseHelper
                        .AgregarAlumnosEscuelaMobile(
                            _escuelaId,
                            seleccionados));

            await DisplayAlert(
                "Añadir estudiantes",
                agregados == 1
                    ? "Se añadió 1 estudiante a la clase."
                    : $"Se añadieron {agregados} estudiantes a la clase.",
                "Aceptar");

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Añadir estudiantes",
                "No se pudieron añadir:\n" + ex.Message,
                "Aceptar");
        }
    }
}


/// <summary>
/// Posible estudiante de la lista de selección.
/// </summary>
public sealed class AlumnoPosibleEscuelaMobile
{
    public Persona Persona { get; set; } = new();

    public string Nombre { get; set; } = "";

    public string Detalle { get; set; } = "";

    public bool Seleccionado { get; set; }
}
