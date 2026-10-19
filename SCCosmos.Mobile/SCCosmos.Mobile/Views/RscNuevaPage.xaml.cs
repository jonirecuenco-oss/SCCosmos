using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Nueva RSC (móvil): día, turno y congregación. Al crear (o si ya
/// existe una ese día y turno) se abre directamente el editor.
/// </summary>
public partial class RscNuevaPage : ContentPage
{
    private bool _cargado;

    private List<Congregacion> _congregaciones =
        new List<Congregacion>();

    public RscNuevaPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_cargado)
        {
            return;
        }

        _cargado =
            true;

        try
        {
            _congregaciones =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerCongregaciones()
                        .OrderBy(c => c.Nombre)
                        .ToList());

            CmbCongregacion.ItemsSource =
                _congregaciones
                    .Select(c => c.Nombre)
                    .ToList();

            // Si ahora mismo hay una visita en curso, se deja ya elegida la
            // congregación visitada.
            try
            {
                DateTime hoy = DateTime.Today;

                int diasDesdeLunes =
                    ((int)hoy.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;

                DateTime martes =
                    hoy.AddDays(-diasDesdeLunes).Date.AddDays(1);

                var semana = await Task.Run(
                    () => DatabaseHelper.ObtenerSemana(
                        martes.ToString("yyyy-MM-dd")));

                if (semana is not null
                    && (semana.Actividad ?? "").Trim().Equals(
                        "Visita",
                        StringComparison.OrdinalIgnoreCase))
                {
                    int indice =
                        _congregaciones.FindIndex(
                            c => string.Equals(
                                c.Nombre,
                                semana.Congregacion,
                                StringComparison.OrdinalIgnoreCase));

                    if (indice >= 0)
                    {
                        CmbCongregacion.SelectedIndex = indice;
                    }
                }
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudieron cargar las congregaciones:\n"
                + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnCrear_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            int indice =
                CmbCongregacion.SelectedIndex;

            if (indice < 0
                || indice >= _congregaciones.Count)
            {
                await DisplayAlert(
                    "RSC",
                    "Elige la congregación.",
                    "Aceptar");

                return;
            }

            Congregacion congregacion =
                _congregaciones[indice];

            string fecha =
                (DpFecha.Date
                 ?? DateTime.Today)
                    .ToString(
                        "yyyy-MM-dd");

            string turno =
                RadTarde.IsChecked
                    ? "Tarde"
                    : "Mañana";

            var existente =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerReunionRscMobile(
                            congregacion.Codigo,
                            fecha,
                            turno));

            string id;

            if (existente != null)
            {
                id =
                    existente.Id;

                await DisplayAlert(
                    "RSC",
                    "Ya existe una RSC de "
                    + congregacion.Nombre
                    + " ese día por la "
                    + turno.ToLower()
                    + "; se abrirá la que ya hay.",
                    "Aceptar");
            }
            else
            {
                id =
                    await Task.Run(
                        () => DatabaseHelper
                            .CrearReunionRscMobile(
                                congregacion.Codigo,
                                fecha,
                                turno));

                if (string.IsNullOrWhiteSpace(
                        id))
                {
                    await DisplayAlert(
                        "RSC",
                        "No se pudo crear la RSC.",
                        "Aceptar");

                    return;
                }

                DatabaseHelper.AnotarActividadMobile(
                    "RSC",
                    "Creó la RSC de "
                    + congregacion.Nombre
                    + " ("
                    + (DpFecha.Date
                       ?? DateTime.Today)
                          .ToString(
                              "dd/MM/yyyy")
                    + " · "
                    + turno
                    + ")");
            }

            await Navigation.PushAsync(
                new RscEditorPage(
                    id));

            Navigation.RemovePage(
                this);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo crear la RSC:\n"
                + ex.Message,
                "Aceptar");
        }
    }
}
