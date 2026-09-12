using System.Collections.ObjectModel;
using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

public partial class InicioPage : ContentPage
{
    public ObservableCollection<ActividadHoy>
        ProgramaHoy
    { get; } = new();

    public InicioPage()
    {
        InitializeComponent();

        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CargarPaginaAsync();
    }

    private async Task CargarPaginaAsync()
    {
        LblError.IsVisible = false;

        try
        {
            if (!DatabaseHelper.ExisteBaseLocal)
            {
                MostrarSinBaseDatos();
                return;
            }

            string nombre =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerConfiguracion(
                        "NombreSC",
                        ""));

            TxtSaludo.Text =
                string.IsNullOrWhiteSpace(nombre)
                    ? "¡Hola!"
                    : $"¡Hola, {nombre}!";

            await Task.WhenAll(
                CargarSemanaActualAsync(),
                CargarEstadisticasAsync());
        }
        catch (Exception ex)
        {
            LblError.Text =
                $"No se pudo cargar Inicio: {ex.Message}";

            LblError.IsVisible = true;
        }
    }

    private async Task CargarSemanaActualAsync()
    {
        DateTime hoy =
            DateTime.Today;

        int diferencia =
            hoy.DayOfWeek -
            DayOfWeek.Tuesday;

        if (diferencia < 0)
            diferencia += 7;

        DateTime martesDeEstaSemana =
            hoy.AddDays(-diferencia).Date;

        string fechaIso =
            martesDeEstaSemana.ToString(
                "yyyy-MM-dd");

        SemanaCalendario? semana =
            await Task.Run(() =>
                DatabaseHelper.ObtenerSemana(
                    fechaIso));

        ProgramaHoy.Clear();

        PanelProgramaDia.IsVisible = false;
        PanelVisita.IsVisible = false;
        PanelOtraActividad.IsVisible = false;
        TxtSinDatos.IsVisible = false;

        if (semana == null)
        {
            LblTipoSemana.Text =
                "SEMANA ACTUAL";

            LblActividad.Text =
                "Sin actividad";

            LblFechas.Text =
                FormatearRango(
                    martesDeEstaSemana);

            TxtSinDatos.IsVisible = true;

            return;
        }

        LblFechas.Text =
            semana.RangoFechas;

        LblActividad.Text =
            string.IsNullOrWhiteSpace(
                semana.Actividad)
                ? "Sin actividad"
                : semana.Actividad;

        if (semana.Actividad == "Visita")
        {
            LblTipoSemana.Text =
                "VISITA A CONGREGACIÓN";

            LblIconoSemana.Text =
                "●";

            PanelVisita.IsVisible = true;

            LblCongregacion.Text =
                semana.Congregacion;

            if (!string.IsNullOrWhiteSpace(
                    semana.Acompanante))
            {
                LblAcompanante.Text =
                    $"Acompañante: {semana.AcompananteVisual}";

                LblAcompanante.IsVisible =
                    true;
            }
            else
            {
                LblAcompanante.IsVisible =
                    false;
            }

            string codigo =
                await Task.Run(() =>
                    DatabaseHelper
                        .ObtenerCodigoCongregacion(
                            semana.Congregacion));

            if (!string.IsNullOrWhiteSpace(codigo))
            {
                string mes =
                    martesDeEstaSemana.ToString(
                        "yyyy-MM");

                await CargarProgramaDelDiaAsync(
                    codigo,
                    mes,
                    hoy.DayOfWeek);
            }
        }
        else
        {
            LblTipoSemana.Text =
                "ACTIVIDAD DE LA SEMANA";

            LblIconoSemana.Text =
                "◆";

            PanelOtraActividad.IsVisible =
                true;

            string detalle =
                semana.CongregacionVisual;

            if (string.IsNullOrWhiteSpace(detalle))
            {
                detalle =
                    semana.Tipo;
            }

            LblOtraActividad.Text =
                detalle;
        }
    }

    private async Task CargarProgramaDelDiaAsync(
        string congreCodigo,
        string mesReferencia,
        DayOfWeek diaHoy)
    {
        string diaDb = diaHoy switch
        {
            DayOfWeek.Wednesday => "Miércoles",
            DayOfWeek.Thursday => "Jueves",
            DayOfWeek.Friday => "Viernes",
            DayOfWeek.Saturday => "Sábado",
            DayOfWeek.Sunday => "Domingo",
            _ => ""
        };

        if (string.IsNullOrEmpty(diaDb))
            return;

        var programa =
            await Task.Run(() =>
            {
                var resultado =
                    new List<ActividadHoy>();

                string[] turnos =
                {
                    "MAÑANA",
                    "MEDIODÍA",
                    "TARDE",
                    "NOCHE"
                };

                string[] responsables =
                {
                    "Joni",
                    "Rebe",
                    "Comida",
                    "Cenar",
                    "Dormir"
                };

                foreach (string turno in turnos)
                {
                    foreach (string responsable
                             in responsables)
                    {
                        var actividades =
                            DatabaseHelper
                                .ObtenerActividadesPrograma(
                                    congreCodigo,
                                    mesReferencia,
                                    diaDb,
                                    turno,
                                    responsable);

                        foreach (var actividad
                                 in actividades)
                        {
                            string nombreCompanero =
                                "";

                            bool mostrarCompanero =
                                responsable != "Comida"
                                && responsable != "Cenar"
                                && responsable != "Dormir";

                            if (mostrarCompanero &&
                                actividad.CompaneroId > 0)
                            {
                                Persona? persona =
                                    DatabaseHelper
                                        .ObtenerPersonaPorId(
                                            actividad.CompaneroId);

                                if (persona != null &&
                                    !string.IsNullOrWhiteSpace(
                                        persona.Nombre))
                                {
                                    nombreCompanero =
                                        persona.Nombre
                                            .Split(
                                                ' ',
                                                StringSplitOptions
                                                    .RemoveEmptyEntries)
                                            .FirstOrDefault()
                                        ?? "";
                                }
                            }

                            resultado.Add(
                                new ActividadHoy
                                {
                                    Hora =
                                        actividad.Hora,

                                    TipoActividad =
                                        actividad.TipoActividad,

                                    CompaneroNombre =
                                        nombreCompanero,

                                    VerCompanero =
                                        mostrarCompanero &&
                                        !string.IsNullOrWhiteSpace(
                                            nombreCompanero)
                                });
                        }
                    }
                }

                return resultado
                    .OrderBy(x => x.Hora)
                    .ToList();
            });

        foreach (ActividadHoy actividad
                 in programa)
        {
            ProgramaHoy.Add(actividad);
        }

        PanelProgramaDia.IsVisible =
            ProgramaHoy.Count > 0;
    }

    private async Task CargarEstadisticasAsync()
    {
        EstadisticasCircuito stats =
            await Task.Run(() =>
                DatabaseHelper
                    .ObtenerEstadisticasCircuito());

        TxtTotalPrecReg.Text =
            stats.PrecRegulares.ToString();

        TxtTotalPrecEsp.Text =
            stats.PrecEspeciales.ToString();

        TxtTotalAncianos.Text =
            stats.Ancianos.ToString();

        TxtTotalSM.Text =
            stats.SiervosM.ToString();

        TxtTotalA2.Text =
            stats.SolA2.ToString();

        TxtTotalA19.Text =
            stats.SolA19.ToString();

        TxtTotalA8.Text =
            stats.SolA8.ToString();

        TxtTotalG8.Text =
            stats.SolG8.ToString();
    }

    private void MostrarSinBaseDatos()
    {
        TxtSaludo.Text =
            "¡Hola!";

        LblActividad.Text =
            "Base de datos no disponible";

        LblFechas.Text =
            "Ve a Configuración y actualiza desde la carpeta sincronizada.";

        PanelVisita.IsVisible = false;
        PanelOtraActividad.IsVisible = false;
        PanelProgramaDia.IsVisible = false;

        TxtSinDatos.IsVisible = true;

        TxtSinDatos.Text =
            "Todavía no hay una base de datos local.";
    }

    private static string FormatearRango(
        DateTime martes)
    {
        DateTime lunes =
            martes.AddDays(-1);

        DateTime domingo =
            martes.AddDays(5);

        return
            $"Del {lunes:dd-MMM} al {domingo:dd-MMM}"
                .ToLower();
    }

    private async void TarjetaEstadistica_Tapped(
    object sender,
    TappedEventArgs e)
    {
        if (e.Parameter is not string parametro)
            return;

        string[] partes =
            parametro.Split(
                '|',
                2,
                StringSplitOptions.None);

        if (partes.Length != 2)
            return;

        string tipo = partes[0];
        string valor = partes[1];

        var filtros =
            new FiltrosPersonas();

        switch (tipo)
        {
            case "Servicio":
                filtros.Servicios.Add(valor);
                break;

            case "Nombramiento":
                filtros.Nombramiento = valor;
                break;

            case "Solicitud":
                filtros.Solicitudes.Add(valor);
                break;

            default:
                return;
        }

        PersonasNavigationState.Establecer(
            filtros);

        await Shell.Current.GoToAsync(
            "//personas");
    }
}