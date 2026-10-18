using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Programa de limpieza de una clase: resumen por días (lunes a
/// sábado) y elección del día de cada estudiante.
/// </summary>
public partial class EscuelaLimpiezaPage : ContentPage, IActualizableMobile
{
    private readonly string _escuelaId;

    private List<LineaLimpiezaMobile> _lineas =
        new();

    private bool _recargando;

    public EscuelaLimpiezaPage(
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

            LblSemana.Text =
                resultado.escuela.SemanaTexto
                + " · cada estudiante limpia el día que le asignes";

            _recargando =
                true;

            _lineas =
                resultado.alumnos
                    .Select(
                        a => new LineaLimpiezaMobile
                        {
                            Alumno =
                                a,
                            Nombre =
                                a.Nombre,
                            DiaSeleccionado =
                                string.IsNullOrWhiteSpace(
                                    a.LimpiezaDia)
                                    ? "Sin limpieza"
                                    : a.LimpiezaDia
                        })
                    .ToList();

            LblSinAlumnos.IsVisible =
                _lineas.Count == 0;

            BindableLayout.SetItemsSource(
                ListaDias,
                _lineas);

            RefrescarResumen();

            _recargando =
                false;
        }
        catch (Exception ex)
        {
            _recargando =
                false;

            await DisplayAlert(
                "Limpieza",
                "No se pudo cargar el programa:\n" + ex.Message,
                "Aceptar");
        }
    }


    private void RefrescarResumen()
    {
        var resumen =
            new List<ResumenDiaMobile>();

        foreach (string dia in
                 new[]
                 {
                     "Lunes",
                     "Martes",
                     "Miércoles",
                     "Jueves",
                     "Viernes",
                     "Sábado"
                 })
        {
            var nombres =
                _lineas
                    .Where(
                        l => string.Equals(
                            l.Alumno.LimpiezaDia,
                            dia,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(
                        l => l.Nombre)
                    .ToList();

            resumen.Add(
                new ResumenDiaMobile
                {
                    Dia =
                        dia,
                    Nombres =
                        nombres.Count == 0
                            ? "— nadie todavía —"
                            : string.Join(
                                ", ",
                                nombres)
                });
        }

        BindableLayout.SetItemsSource(
            ResumenDias,
            resumen);
    }


    private async void BtnPdfLimpieza_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            var escuela =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerEscuelaClaseMobile(
                            _escuelaId));

            if (escuela is null)
            {
                return;
            }

            var alumnos =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerAlumnosEscuelaMobile(
                            _escuelaId));

            string ruta =
                System.IO.Path.Combine(
                    FileSystem.CacheDirectory,
                    "Limpieza_"
                    + escuela.SemanaInicio
                    + ".pdf");

            await Task.Run(
                () => ProgramaEscuelaPdfService.GenerarLimpieza(
                    escuela,
                    alumnos,
                    ruta));

            DatabaseHelper.AnotarActividadMobile(
                "Escuelas",
                "Generó el PDF del programa de limpieza de la escuela de la "
                + escuela.SemanaTexto.ToLower());

            await Launcher.Default.OpenAsync(
                new OpenFileRequest(
                    "Programa de limpieza",
                    new ReadOnlyFile(ruta)));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "PDF",
                "No se pudo generar el PDF:\n" + ex.Message,
                "Aceptar");
        }
    }


    private async void PickerDia_Changed(
        object sender,
        EventArgs e)
    {
        try
        {
            if (_recargando)
            {
                return;
            }

            if (sender is not Picker picker
                || picker.BindingContext is not LineaLimpiezaMobile linea
                || picker.SelectedItem is not string dia)
            {
                return;
            }

            string diaReal =
                dia == "Sin limpieza"
                    ? ""
                    : dia;

            if (string.Equals(
                    linea.Alumno.LimpiezaDia,
                    diaReal,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            linea.Alumno.LimpiezaDia =
                diaReal;

            await Task.Run(
                () => DatabaseHelper
                    .AsignarLimpiezaEscuelaMobile(
                        linea.Alumno.Id,
                        diaReal));

            RefrescarResumen();
        }
        catch
        {
        }
    }
}


/// <summary>
/// Una fila de estudiante con su día de limpieza.
/// </summary>
public sealed class LineaLimpiezaMobile
{
    public EscuelaAlumnoMobile Alumno { get; set; } = new();

    public string Nombre { get; set; } = "";

    public string DiaSeleccionado { get; set; } = "Sin limpieza";

    public List<string> Opciones =>
        new()
        {
            "Sin limpieza",
            "Lunes",
            "Martes",
            "Miércoles",
            "Jueves",
            "Viernes",
            "Sábado"
        };
}


/// <summary>
/// Una línea del resumen por días.
/// </summary>
public sealed class ResumenDiaMobile
{
    public string Dia { get; set; } = "";

    public string Nombres { get; set; } = "";
}
