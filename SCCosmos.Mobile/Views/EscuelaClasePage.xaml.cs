using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Detalle de una clase (escuela de una semana): información, acciones
/// (estudiantes, asientos, limpieza) y listado de estudiantes.
/// </summary>
public partial class EscuelaClasePage : ContentPage, IActualizableMobile
{
    private readonly string _escuelaId;

    private EscuelaClaseMobile? _escuela;

    public EscuelaClasePage(
        string escuelaId)
    {
        InitializeComponent();

        _escuelaId =
            escuelaId ?? "";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CargarAsync();
    }

    public void DatosActualizados()
    {
        try
        {
            _ = CargarAsync();
        }
        catch
        {
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
                        EscuelaClaseMobile escuela =
                            DatabaseHelper
                                .ObtenerEscuelaClaseMobile(
                                    _escuelaId);

                        List<EscuelaAlumnoMobile> alumnos =
                            DatabaseHelper
                                .ObtenerAlumnosEscuelaMobile(
                                    _escuelaId);

                        return (escuela, alumnos);
                    });

            if (resultado.escuela is null)
            {
                await Navigation.PopAsync();

                return;
            }

            _escuela =
                resultado.escuela;

            LblSemana.Text =
                _escuela.SemanaTexto;

            LblInfo.Text =
                "Salón: "
                + (string.IsNullOrWhiteSpace(
                       _escuela.Congregacion)
                       ? "sin indicar"
                       : _escuela.Congregacion)
                + " · Instructor: "
                + _escuela.InstructorTexto
                + " · Sustituto: "
                + _escuela.SustitutoTexto
                + " · "
                + (_escuela.Alumnos == 1
                       ? "1 estudiante"
                       : _escuela.Alumnos + " estudiantes")
                + " · "
                + (_escuela.Asientos < 1
                       ? 12
                       : _escuela.Asientos)
                + " butacas";

            LblNotas.Text =
                _escuela.NotasTexto;

            List<EscuelaAlumnoMobile> alumnos =
                resultado.alumnos;

            LblSinAlumnos.IsVisible =
                alumnos.Count == 0;

            BindableLayout.SetItemsSource(
                ListaAlumnos,
                alumnos);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Clase",
                "No se pudo cargar la clase:\n" + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnEditar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_escuela is null)
        {
            return;
        }

        await Navigation.PushAsync(
            new EscuelaEdicionPage(
                _escuela));
    }


    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_escuela is null)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Eliminar escuela",
                $"¿Eliminar la {_escuela.SemanaTexto.ToLower()}? "
                + "Se quitarán también sus estudiantes apuntados.",
                "Eliminar",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        try
        {
            await Task.Run(
                () => DatabaseHelper
                    .EliminarEscuelaClaseMobile(
                        _escuela.Id));

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Eliminar escuela",
                "No se pudo eliminar:\n" + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new EscuelaAlumnosPickerPage(
                _escuelaId));
    }


    private async void BtnAsientos_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new EscuelaAsientosPage(
                _escuelaId));
    }


    private async void BtnLimpieza_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new EscuelaLimpiezaPage(
                _escuelaId));
    }


    private async void BtnProximamente_Clicked(
        object sender,
        EventArgs e)
    {
        await DisplayAlert(
            "Próximamente",
            "Esta parte se programará más adelante (lo dejamos preparado en la clase).",
            "Aceptar");
    }


    private async void BtnTalleres_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new EscuelaTalleresPage(
                _escuelaId));
    }


    private async void BtnHorario_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new EscuelaHorarioPage(
                _escuelaId));
    }


    private async void BtnQuitarAlumno_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            || boton.CommandParameter is not EscuelaAlumnoMobile alumno)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Quitar estudiante",
                $"¿Quitar a {alumno.Nombre} de esta clase?",
                "Quitar",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        try
        {
            await Task.Run(
                () => DatabaseHelper
                    .QuitarAlumnoEscuelaMobile(
                        alumno.Id));

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Quitar estudiante",
                "No se pudo quitar:\n" + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnVolver_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
