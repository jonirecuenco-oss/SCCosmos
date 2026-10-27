using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

#if ANDROID
using SCCosmos.Mobile.Platforms.Android;
#endif

namespace SCCosmos.Mobile.Views;


public partial class VisitaDetallePage
    : ContentPage
{
    private readonly SemanaCalendario _semana;

    private VistaVisitaMobile? _vista;

    private List<PersonaVisitaUi> _personasUi = new();
    private List<VistaPersonaVisitaMobile> _personasMinisterio = new();
    private List<ProgramaVisitaUi> _programaUi = new();
    private List<TerritorioUi> _territoriosUi = new();

    private const int TerritoriosPorPagina = 25;

    private int _paginaTerritorios;
    private List<RecomendacionS2Ui> _s2Ui = new();

    private readonly Dictionary<int, CancellationTokenSource>
        _guardadosNotaPendientes = new();

    private DateTime _mesMinisterio;
    private DateTime _mesContabilidad;

    private DateTime? _maxMesActividad;

    private bool _cargado;
    private bool _recargarMinisterioAlVolver;
    private bool _programaPreparado;
    private bool _notasPreparadas;
    private bool _territoriosPreparados;
    private bool _mapaPreparado;
    private bool _contabilidadPreparada;
    private bool _s303Preparado;

    private DocumentoVisitaMobile? _documentoS303;
    private DocumentoVisitaMobile? _plantillaS303;


    public VisitaDetallePage(
        SemanaCalendario semana)
    {
        InitializeComponent();

        _semana = semana;

        LblCongregacion.Text = semana.Congregacion ?? "";
        LblPeriodo.Text = semana.RangoFechas;

        _mesMinisterio =
            new DateTime(
                semana.FechaMartes.Year,
                semana.FechaMartes.Month,
                1);

        _mesContabilidad =
            _mesMinisterio;

        // El perfil limitado solo ve los datos de la congregación, el
        // resumen, el ministerio (sin los cuadros de las horas) y el
        // programa de la visita.
        AplicarPerfilAcceso();
    }


    private void AplicarPerfilAcceso()
    {
        if (PerfilAccesoService.AccesoCompleto)
        {
            return;
        }

        BtnNotas.IsVisible = false;
        BtnTerritorios.IsVisible = false;
        BtnMapa.IsVisible = false;
        BtnContabilidad.IsVisible = false;
        BtnS303.IsVisible = false;

        // Tampoco ve esas pestañas ni el selector de meses.
        TabNotas.IsVisible = false;
        TabTerritorios.IsVisible = false;
        TabMapa.IsVisible = false;
        TabContabilidad.IsVisible = false;
        TabS303.IsVisible = false;

        CintaMesMinisterio.IsVisible = false;
    }


    // ========================================================================
    // PESTAÑAS
    // ========================================================================

    private async void Tab_Clicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not Button boton
            || boton.CommandParameter is not string clave)
        {
            return;
        }

        await MostrarPestanaAsync(clave);
    }


    private async Task MostrarPestanaAsync(string clave)
    {
        SeccionMinisterio.IsVisible = clave == "ministerio";
        SeccionMapa.IsVisible = clave == "mapa";
        SeccionPrograma.IsVisible = clave == "programa";
        SeccionNotas.IsVisible = clave == "notas";
        SeccionInformacion.IsVisible = clave == "informacion";
        SeccionGrupos.IsVisible = clave == "grupos";
        SeccionTerritorios.IsVisible = clave == "territorios";
        SeccionContabilidad.IsVisible = clave == "contabilidad";
        SeccionS303.IsVisible = clave == "s303";

        PintarPestanaSeleccionada(clave);

        try
        {
            await ScrollContenido.ScrollToAsync(
                0,
                0,
                false);
        }
        catch
        {
        }

        switch (clave)
        {
            case "programa":
                if (!_programaPreparado)
                {
                    PintarPrograma();
                }
                break;

            case "notas":
                await PrepararNotasAsync();
                break;

            case "territorios":
                if (!_territoriosPreparados)
                {
                    PintarTerritorios();
                }
                break;

            case "mapa":
                if (!_mapaPreparado)
                {
                    _mapaPreparado = true;
                    await CargarMapaVisitaAsync();
                }
                break;

            case "contabilidad":
                if (!_contabilidadPreparada)
                {
                    _contabilidadPreparada = true;
                    await CargarContabilidadEditableAsync();
                }
                break;

            case "s303":
                if (!_s303Preparado)
                {
                    await CargarS303yS2Async();
                }
                break;
        }
    }


    private void PintarPestanaSeleccionada(
        string clave)
    {
        var botones =
            new (string Clave, Button Boton)[]
            {
                ("ministerio", TabMinisterio),
                ("mapa", TabMapa),
                ("programa", TabPrograma),
                ("notas", TabNotas),
                ("informacion", TabInformacion),
                ("grupos", TabGrupos),
                ("territorios", TabTerritorios),
                ("contabilidad", TabContabilidad),
                ("s303", TabS303)
            };

        foreach ((string claveBoton, Button boton) in botones)
        {
            bool activa =
                claveBoton == clave;

            boton.BackgroundColor =
                Color.FromArgb(
                    activa
                        ? "#1B4E7A"
                        : "#1A1D22");

            boton.TextColor =
                activa
                    ? Colors.White
                    : Color.FromArgb("#9CC3F5");

            boton.FontAttributes =
                activa
                    ? FontAttributes.Bold
                    : FontAttributes.None;
        }
    }


    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_cargado)
        {
            _cargado = true;

            var reloj = System.Diagnostics.Stopwatch.StartNew();
            await CargarAsync();
            Services.Perf.Registrar(
                "VisitaDetalle", reloj.ElapsedMilliseconds);

            return;
        }

        if (_recargarMinisterioAlVolver)
        {
            _recargarMinisterioAlVolver = false;

            await CambiarMesMinisterioAsync(0);
        }
    }


    private async Task CargarAsync()
    {
        MostrarCarga("Cargando visita...");

        try
        {
            var relojDb =
                System.Diagnostics.Stopwatch.StartNew();

            _vista =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerVistaVisitaMobile(
                        _semana.Congregacion ?? "",
                        _semana.FechaMartes));

            Services.Perf.Registrar(
                "Visita.Vista",
                relojDb.ElapsedMilliseconds);

            if (string.IsNullOrWhiteSpace(
                    _vista.CongregacionCodigo))
            {
                await DisplayAlert(
                    "Visita",
                    "No se ha encontrado la congregación asignada a esta visita.",
                    "Aceptar");

                return;
            }

            PintarCabecera();
            PintarResumen();

            var relojMax =
                System.Diagnostics.Stopwatch.StartNew();

            _maxMesActividad =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerMaximoMesActividadMobile(
                                _vista.CongregacionCodigo));

            Services.Perf.Registrar(
                "Visita.MaxMes",
                relojMax.ElapsedMilliseconds);

            var relojPersonas =
                System.Diagnostics.Stopwatch.StartNew();

            // Si el mes de Ministerio coincide con el de la visita, la
            // lista ya se cargó con la vista: se reutiliza (ahorra una
            // segunda carga completa de personas y actividad).
            DateTime mesVisita =
                new(
                    _semana.FechaMartes.Year,
                    _semana.FechaMartes.Month,
                    1);

            DateTime mesActual =
                new(
                    DateTime.Today.Year,
                    DateTime.Today.Month,
                    1);

            DateTime finVista =
                mesVisita > mesActual
                    ? mesActual
                    : mesVisita;

            // La vista termina los cuadros en el último mes con datos.
            if (_maxMesActividad.HasValue
                && _maxMesActividad.Value < finVista)
            {
                finVista = _maxMesActividad.Value;
            }

            DateTime finCuadros =
                MesFinCuadrosMinisterio();

            bool mismaVentana =
                _mesMinisterio.Year == mesVisita.Year
                && _mesMinisterio.Month == mesVisita.Month
                && finCuadros.Year == finVista.Year
                && finCuadros.Month == finVista.Month;

            _personasMinisterio =
                mismaVentana
                    ? _vista.Personas
                    : await Task.Run(
                        () =>
                            DatabaseHelper
                                .ObtenerPersonasMinisterioVisitaMobile(
                                    _vista.CongregacionCodigo,
                                    _mesMinisterio,
                                    finCuadros));

            Services.Perf.Registrar(
                "Visita.Personas",
                relojPersonas.ElapsedMilliseconds);

            var relojPintar =
                System.Diagnostics.Stopwatch.StartNew();

            // Por defecto, el mes de Ministerio es el último con
            // actividad (no el mes de la visita si aún no hay datos).
            if (_maxMesActividad.HasValue
                && _mesMinisterio > _maxMesActividad.Value)
            {
                _mesMinisterio = _maxMesActividad.Value;
            }

            PintarGrupos();
            PintarMesMinisterio();
            PintarPersonas();
            PrepararPrograma();
            PrepararTerritorios();

            BtnPrograma.Text =
                $"Programa de la visita  ({_programaUi.Count})";

            BtnNotas.Text =
                $"Notas personales de la visita  ({ContarNotasConContenido()})";

            BtnTerritorios.Text =
                $"Territorios (S-13)  ({_territoriosUi.Count})";

            BtnContabilidad.Text =
                "Contabilidad · últimos 6 meses";

            BtnS303.Text =
                "S-303 y S-2";

            Services.Perf.Registrar(
                "Visita.Pintar",
                relojPintar.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Visita",
                "No se pudieron cargar los datos de la visita:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            OcultarCarga();
        }
    }


    // ========================================================================
    // CABECERA / CONGREGACIÓN
    // ========================================================================

    private void PintarCabecera()
    {
        if (_vista is null)
        {
            return;
        }

        LblCongregacion.Text =
            _vista.CongregacionNombre;

        string reuniones =
            string.Join(
                "\n",
                new[]
                {
                    string.IsNullOrWhiteSpace(_vista.ReunionEntreSemana)
                        ? ""
                        : "Entre semana: " + _vista.ReunionEntreSemana,

                    string.IsNullOrWhiteSpace(_vista.ReunionFinSemana)
                        ? ""
                        : "Fin de semana: " + _vista.ReunionFinSemana
                }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        LblReuniones.Text = reuniones;
        LblReuniones.IsVisible =
            !string.IsNullOrWhiteSpace(reuniones);

        LblEmail.Text = string.IsNullOrWhiteSpace(_vista.EmailCongregacion)
            ? ""
            : "Email: " + _vista.EmailCongregacion;

        LblEmail.IsVisible =
            !string.IsNullOrWhiteSpace(LblEmail.Text);

        try
        {
            var datosZoom =
                DatabaseHelper.ObtenerDatosExtraCongregacionMobile(
                    _vista.CongregacionCodigo);

            LblEmailAV.Text =
                string.IsNullOrWhiteSpace(datosZoom.EmailAV)
                    ? ""
                    : "Email de AV: " + datosZoom.EmailAV;

            LblEmailAV.IsVisible =
                !string.IsNullOrWhiteSpace(LblEmailAV.Text);
        }
        catch
        {
            LblEmailAV.IsVisible = false;
        }
    }


    private void BtnDatos_Clicked(
        object sender,
        EventArgs e)
    {
        PanelDatosCongregacion.IsVisible =
            !PanelDatosCongregacion.IsVisible;
    }


    // ========================================================================
    // RESUMEN
    // ========================================================================

    private void PintarResumen()
    {
        if (_vista is null)
        {
            return;
        }

        // Publicadores activos con grupo (los inactivos y las fichas sin
        // grupo no cuentan), igual que «Publicadores activos» en Windows
        // y que la suma de los grupos.
        LblPublicadores.Text =
            _vista.TotalPersonas.ToString();

        LblEdad.Text =
            _vista.MediaEdad > 0
                ? _vista.MediaEdad.ToString(
                    "0.0",
                    CultureInfo.CurrentCulture)
                : "-";

        LblNombrados.Text =
            $"{_vista.Ancianos} / {_vista.SiervosMinisteriales}";

        LblPrecursores.Text =
            $"{_vista.PrecursoresRegulares} / {_vista.PrecursoresEspeciales}";

        LblSexo.Text =
            $"{_vista.TotalHombres} / {_vista.TotalMujeres}";

        LblNoBautizados.Text =
            _vista.NoBautizados.ToString();
    }


    // ========================================================================
    // GRUPOS
    // ========================================================================

    private void PintarGrupos()
    {
        if (_vista is null)
        {
            return;
        }

        ListaGrupos.ItemsSource = _vista.Grupos;
    }


    // ========================================================================
    // MINISTERIO
    // ========================================================================

    private void PintarPersonas()
    {
        if (_vista is null)
        {
            return;
        }

        // Los inactivos van al final de la lista.
        _personasUi =
            _personasMinisterio
                .OrderBy(p => p.EsInactivo)
                .ThenBy(p => p.Grupo)
                .ThenBy(p => p.NombreVisual)
                .Select(CrearPersonaUi)
                .ToList();

        ActualizarPickerGrupoMinisterio();
        AplicarFiltroGrupo();
    }


    private void PintarMesMinisterio()
    {
        // Abreviado para que quepa junto al selector de grupos
        // («Sept 2026», «Oct 2026»…).
        LblMesMinisterio.Text =
            FormatearMesCorto(
                _mesMinisterio);
    }


    private static string FormatearMesCorto(DateTime fecha)
    {
        string texto =
            fecha.ToString(
                "MMM yyyy",
                new CultureInfo("es-ES"));

        return texto.Length == 0
            ? ""
            : char.ToUpper(texto[0]) + texto.Substring(1);
    }


    private void ActualizarPickerGrupoMinisterio()
    {
        string? seleccionado =
            PickerGrupo.SelectedItem as string;

        List<string> opciones =
            _personasMinisterio
                .Where(p => p.Grupo > 0)
                .Select(p => p.Grupo)
                .Distinct()
                .OrderBy(g => g)
                .Select(g => $"Grupo {g}")
                .ToList();

        if (_personasMinisterio.Any(p => p.Grupo <= 0))
        {
            opciones.Add("Sin asignar");
        }

        PickerGrupo.ItemsSource = opciones;

        if (!string.IsNullOrWhiteSpace(seleccionado)
            &&
            opciones.Contains(seleccionado))
        {
            PickerGrupo.SelectedItem = seleccionado;
        }
        else
        {
            PickerGrupo.SelectedIndex =
                opciones.Count > 0
                    ? 0
                    : -1;
        }
    }


    private async void BtnMesAntMinisterio_Clicked(
        object sender,
        EventArgs e)
    {
        await CambiarMesMinisterioAsync(-1);
    }


    private async void BtnMesSigMinisterio_Clicked(
        object sender,
        EventArgs e)
    {
        await CambiarMesMinisterioAsync(1);
    }


    private async Task CambiarMesMinisterioAsync(
        int desplazamiento)
    {
        if (_vista is null)
        {
            return;
        }

        DateTime anterior =
            _mesMinisterio;

        _mesMinisterio =
            _mesMinisterio.AddMonths(
                desplazamiento);

        MostrarCarga("Cargando ministerio...");

        try
        {
            List<VistaPersonaVisitaMobile> personas =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerPersonasMinisterioVisitaMobile(
                        _vista.CongregacionCodigo,
                        _mesMinisterio,
                        MesFinCuadrosMinisterio()));

            _personasMinisterio = personas;
            PintarMesMinisterio();
            PintarPersonas();
        }
        catch (Exception ex)
        {
            _mesMinisterio = anterior;
            PintarMesMinisterio();

            await DisplayAlert(
                "Ministerio",
                "No se pudo cargar el mes seleccionado:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            OcultarCarga();
        }
    }


    /// <summary>
    /// Mes en el que terminan los cuadros de los 6 meses: el último mes con
    /// registro de actividad, y nunca después del mes seleccionado (misma
    /// regla que usa Windows).
    /// </summary>
    private DateTime MesFinCuadrosMinisterio()
    {
        return _maxMesActividad.HasValue
            && _mesMinisterio > _maxMesActividad.Value
                ? _maxMesActividad.Value
                : _mesMinisterio;
    }


    /// <summary>
    /// Al pulsar una persona se abre su ficha para poder editarla.
    /// </summary>
    private async void PersonaVisita_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not int personaId
            || personaId <= 0)
        {
            return;
        }

        MostrarCarga("Abriendo persona...");

        try
        {
            int idActual =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerIdPersonaActualMobile(
                                personaId));

            if (idActual <= 0)
            {
                return;
            }

            await Navigation.PushAsync(
                new PersonaDetallePage(
                    idActual));

            // Al volver de la ficha se refresca el mes para reflejar los
            // cambios (nombre, grupo, foto...).
            _recargarMinisterioAlVolver = true;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Visita",
                "No se pudo abrir la persona:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            OcultarCarga();
        }
    }


    private PersonaVisitaUi CrearPersonaUi(
        VistaPersonaVisitaMobile p)
    {
        List<string> datosMinisterio = new();

        if (!string.IsNullOrWhiteSpace(p.Nombramiento)
            &&
            !string.Equals(
                p.Nombramiento.Trim(),
                "Publicador",
                StringComparison.OrdinalIgnoreCase))
        {
            datosMinisterio.Add(p.Nombramiento.Trim());
        }

        if (EsPrecursor(p.Servicio))
        {
            datosMinisterio.Add(p.Servicio.Trim());
        }

        if (p.EsInactivo)
        {
            datosMinisterio.Add("INACTIVO");
        }

        string nombramiento =
            (p.Nombramiento ?? "")
                .Trim()
                .ToLowerInvariant();

        string servicio =
            (p.Servicio ?? "")
                .Trim()
                .ToLowerInvariant();

        string etiquetaNombramiento =
            nombramiento.Contains("anciano")
                ? "A"
                : (nombramiento.Contains("siervo")
                   || nombramiento.Contains("ministerial"))
                    ? "SM"
                    : "";

        string etiquetaServicio =
            servicio.Contains("especial")
                ? "PEsp"
                : servicio.Contains("misionero")
                    ? "Mis."
                    : servicio.Contains("precursor")
                        ? "PR"
                        : "";

        var ui =
            new PersonaVisitaUi
            {
                PersonaId = p.PersonaActividadId,
                MostrarCuadrosHoras = PerfilAccesoService.AccesoCompleto,
                Grupo = p.Grupo,
                NombreVisual = p.NombreVisual,
                EtiquetaNombramiento = etiquetaNombramiento,
                EtiquetaServicio = etiquetaServicio,
                DatosMinisterio =
                    string.Join(" · ", datosMinisterio),
                FotoSource = CrearFotoSource(p.Foto),
                TieneAlertaHoras = p.TieneAlertaHoras,
                EsInactivo = p.EsInactivo,
                RequisitoHoras = p.RequisitoHorasCalculado,
                HorasAcumuladas = p.HorasServicioAcumuladas,
                MetaHoras = p.MetaHorasProrrateada,
                DeficitHoras = p.DeficitHoras
            };

        List<ActividadMesUi> actividad =
            p.Actividad
                .OrderBy(a => a.Mes)
                .Select(CrearActividadUi)
                .ToList();

        while (actividad.Count < 6)
        {
            actividad.Insert(
                0,
                ActividadMesUi.Vacia());
        }

        actividad =
            actividad
                .TakeLast(6)
                .ToList();

        ui.Actividad1 = actividad[0];
        ui.Actividad2 = actividad[1];
        ui.Actividad3 = actividad[2];
        ui.Actividad4 = actividad[3];
        ui.Actividad5 = actividad[4];
        ui.Actividad6 = actividad[5];

        return ui;
    }


    private static ActividadMesUi CrearActividadUi(
        VistaActividadMesMobile act)
    {
        string horas =
            NormalizarHorasVisuales(act.Horas);

        string valor;
        string indicador = "";
        string fondo;
        string texto;

        if (!string.IsNullOrWhiteSpace(horas))
        {
            valor = horas + "h";
            fondo = "#163553";
            texto = "#B9DEFF";
        }
        else if (act.Participacion)
        {
            valor = "✓";
            fondo = "#173A25";
            texto = "#8EE29D";
        }
        else if (act.ExisteRegistro)
        {
            valor = "✕";
            fondo = "#3A2024";
            texto = "#FF9AA3";
        }
        else
        {
            valor = "–";
            fondo = "#242830";
            texto = "#8F969F";
        }

        // El indicador «PA» ya no se muestra en la tarjeta (no aporta
        // nada y ensuciaba la etiqueta del mes).
        indicador = "";

        string etiqueta =
            act.Mes == default
                ? ""
                : Capitalizar(
                    act.Mes.ToString(
                        "MMM yy",
                        CultureInfo.GetCultureInfo("es-ES")));

        return new ActividadMesUi
        {
            Mes = etiqueta,
            Valor = valor,
            Indicador = indicador,
            Notas = act.Notas ?? "",
            TieneNotas =
                !string.IsNullOrWhiteSpace(act.Notas),
            ValorFontSize =
                valor is "✓" or "✕"
                    ? 16
                    : 14,
            Acumulado = act.AcumuladoTexto,
            AcumuladoColor =
                Color.FromArgb(
                    string.IsNullOrWhiteSpace(act.AcumuladoColor)
                        ? "#9EA4AE"
                        : act.AcumuladoColor),
            Fondo = Color.FromArgb(fondo),
            TextoColor = Color.FromArgb(texto)
        };
    }


    private async void ActividadMes_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter
                is not ActividadMesUi actividad)
        {
            return;
        }

        await MostrarNotaActividadAsync(
            actividad);
    }


    private async void ActividadMes_Tapped(
        object sender,
        TappedEventArgs e)
    {
        // En Android el parámetro fiable del gesto está en TappedEventArgs.
        // Dejamos también el fallback al recognizer por compatibilidad.
        ActividadMesUi? actividad =
            e.Parameter as ActividadMesUi;

        if (actividad is null
            &&
            sender is TapGestureRecognizer gesto)
        {
            actividad =
                gesto.CommandParameter as ActividadMesUi;
        }

        if (actividad is null
            ||
            !actividad.TieneNotas)
        {
            return;
        }

        await MostrarNotaActividadAsync(
            actividad);
    }


    private async void NotaActividad_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter
                is not ActividadMesUi actividad
            ||
            !actividad.TieneNotas)
        {
            return;
        }

        await MostrarNotaActividadAsync(
            actividad);
    }


    private async Task MostrarNotaActividadAsync(
        ActividadMesUi actividad)
    {
        string nota =
            string.IsNullOrWhiteSpace(
                actividad.Notas)
                ? "No hay notas registradas para este mes."
                : actividad.Notas.Trim();

        await DisplayAlert(
            string.IsNullOrWhiteSpace(
                actividad.Mes)
                ? "Actividad del mes"
                : "Actividad · " + actividad.Mes,
            nota,
            "Cerrar");
    }


    private static string NormalizarHorasVisuales(
        string? horas)
    {
        string valor =
            (horas ?? "")
                .Replace(" ", "")
                .Trim();

        if (string.IsNullOrWhiteSpace(valor)
            ||
            valor is "0" or "0.0" or "0,0")
        {
            return "";
        }

        return valor;
    }


    private static bool EsPrecursor(
        string? servicio)
    {
        if (string.IsNullOrWhiteSpace(servicio))
        {
            return false;
        }

        return servicio.Contains(
                   "precursor",
                   StringComparison.OrdinalIgnoreCase)
               ||
               servicio.Contains(
                   "misionero",
                   StringComparison.OrdinalIgnoreCase);
    }


    private async void AdvertenciaHoras_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not PersonaVisitaUi persona)
        {
            return;
        }

        await DisplayAlert(
            "Seguimiento de horas",
            $"{persona.NombreVisual}\n\n" +
            $"Meta prorrateada: {persona.MetaHoras:0.#} h\n" +
            $"Horas logradas: {persona.HorasAcumuladas:0.#} h\n" +
            $"Déficit actual: {persona.DeficitHoras:0.#} h\n" +
            $"Meta anual asignada: {persona.RequisitoHoras} h",
            "Aceptar");
    }


    private void PickerGrupo_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        AplicarFiltroGrupo();
    }


    private void AplicarFiltroGrupo()
    {
        if (_personasUi.Count == 0)
        {
            MostrarPersonas(
                _personasUi);

            return;
        }

        if (PickerGrupo.SelectedItem is not string textoGrupo)
        {
            MostrarPersonas(
                _personasUi);

            return;
        }

        if (string.Equals(
                textoGrupo,
                "Sin asignar",
                StringComparison.OrdinalIgnoreCase))
        {
            MostrarPersonas(
                _personasUi
                    .Where(p => p.Grupo <= 0)
                    .ToList());

            return;
        }

        if (!int.TryParse(
                new string(
                    textoGrupo
                        .Where(char.IsDigit)
                        .ToArray()),
                out int grupo))
        {
            MostrarPersonas(
                _personasUi);

            return;
        }

        MostrarPersonas(
            _personasUi
                .Where(p => p.Grupo == grupo)
                .ToList());
    }


    /// <summary>
    /// Pinta las personas con un apartado claro para los inactivos (no
    /// cuentan en los totales ni en los grupos, pero se siguen viendo).
    /// </summary>
    private void MostrarPersonas(
        List<PersonaVisitaUi> personas)
    {
        var filas =
            new List<PersonaVisitaUi>();

        filas.AddRange(
            personas.Where(p => !p.EsInactivo));

        List<PersonaVisitaUi> inactivos =
            personas
                .Where(p => p.EsInactivo)
                .ToList();

        if (inactivos.Count > 0)
        {
            filas.Add(
                new PersonaVisitaUi
                {
                    EsSeparador = true,
                    TextoSeparador =
                        $"PUBLICADORES INACTIVOS ({inactivos.Count})"
                });

            filas.AddRange(
                inactivos);
        }

        ListaPersonas.ItemsSource =
            filas;
    }


    // ========================================================================
    // PROGRAMA DE LA VISITA
    // ========================================================================

    private void PrepararPrograma()
    {
        if (_vista is null)
        {
            return;
        }

        _programaUi =
            _vista.Programa
                .Select(CrearProgramaUi)
                .ToList();

        _programaPreparado = false;
    }


    private static ProgramaVisitaUi CrearProgramaUi(
        VistaProgramaVisitaMobile p)
    {
        string subtitulo =
            string.Join(
                " · ",
                new[]
                {
                    p.Responsable,
                    p.TipoActividad,
                    p.Companero
                }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        return new ProgramaVisitaUi
        {
            Original = p,
            Hora = p.Hora,
            Titulo =
                string.Join(
                    " · ",
                    new[] { p.Dia, p.Turno }
                        .Where(x => !string.IsNullOrWhiteSpace(x))),
            Subtitulo = subtitulo,
            Ubicacion = p.Ubicacion,
            Notas = p.Notas
        };
    }


    private void PintarPrograma()
    {
        _programaPreparado = true;
        ListaPrograma.ItemsSource = null;
        ListaPrograma.ItemsSource = _programaUi;
        LblSinPrograma.IsVisible = _programaUi.Count == 0;
    }


    private void BtnPrograma_Clicked(
        object sender,
        EventArgs e)
    {
        PanelPrograma.IsVisible = !PanelPrograma.IsVisible;

        if (PanelPrograma.IsVisible)
        {
            PintarPrograma();
        }
    }


    private async void BtnNuevaActividad_Clicked(
        object sender,
        EventArgs e)
    {
        await AbrirEditorActividadAsync(null);
    }


    private async void BtnMapaPrograma_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            || boton.CommandParameter is not ProgramaVisitaUi fila)
        {
            return;
        }

        string consulta = "";

        if (fila.Original.Latitud != 0
            || fila.Original.Longitud != 0)
        {
            consulta =
                fila.Original.Latitud.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ","
                + fila.Original.Longitud.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (!string.IsNullOrWhiteSpace(
                     fila.Original.Ubicacion))
        {
            consulta =
                fila.Original.Ubicacion.Trim();
        }

        if (consulta.Length == 0)
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(
                "https://www.google.com/maps/search/?api=1&query=" +
                Uri.EscapeDataString(consulta));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Mapa",
                "No se pudo abrir el mapa:\n\n" + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnImportarPrograma_Clicked(
        object sender,
        EventArgs e)
    {
        if (_vista is null)
        {
            return;
        }

        try
        {
            var archivo =
                await FilePicker.Default.PickAsync(
                    new PickOptions
                    {
                        PickerTitle =
                            "Selecciona el JSON del programa"
                    });

            if (archivo is null)
            {
                return;
            }

            string contenido =
                await File.ReadAllTextAsync(
                    archivo.FullPath);

            List<Persona> personas =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerPersonasPorCongregacion(
                                _vista.CongregacionCodigo));

            ProgramaImportService.ResultadoImportacionPrograma
                resultado =
                    ProgramaImportService.Parsear(
                        contenido,
                        personas);

            if (resultado.Actividades.Count == 0)
            {
                await DisplayAlert(
                    "Importar programa",
                    "No se ha encontrado ninguna actividad válida " +
                    "para importar.",
                    "Aceptar");

                return;
            }

            int existentes =
                _vista.Programa?.Count() ?? 0;

            bool reemplazar =
                false;

            if (existentes > 0)
            {
                string decision =
                    await DisplayActionSheet(
                        "Ya existe actividad en el programa de esta " +
                        "visita.",
                        "Cancelar",
                        null,
                        "Reemplazar todo el programa",
                        "Añadir al programa actual");

                if (decision == "Reemplazar todo el programa")
                {
                    reemplazar = true;
                }
                else if (decision != "Añadir al programa actual")
                {
                    return;
                }
            }

            int insertadas =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ImportarProgramaVisitaMobile(
                                _vista.CongregacionCodigo,
                                _vista.MesReferencia,
                                resultado.Actividades,
                                reemplazar));

            await RecargarProgramaAsync();

            string resumen =
                $"Se han importado {insertadas} actividades.";

            if (resultado.Avisos.Count > 0)
            {
                resumen +=
                    "\n\nAvisos:\n• " +
                    string.Join(
                        "\n• ",
                        resultado.Avisos.Take(8));
            }

            await DisplayAlert(
                "Programa importado",
                resumen,
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Importar programa",
                "No se pudo importar el programa JSON:\n\n" +
                ex.Message,
                "Aceptar");
        }
    }


    private async void BtnEditarActividad_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is ProgramaVisitaUi fila)
        {
            await AbrirEditorActividadAsync(fila.Original);
        }
    }


    private async Task AbrirEditorActividadAsync(
        VistaProgramaVisitaMobile? actividad)
    {
        if (_vista is null)
        {
            return;
        }

        var pagina =
            new VisitaProgramaActividadPage(
                _vista.CongregacionCodigo,
                _vista.MesReferencia,
                _vista.Personas,
                actividad,
                RecargarProgramaAsync);

        await Navigation.PushAsync(pagina);
    }


    private async void BtnEliminarActividad_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not ProgramaVisitaUi fila)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Programa de la visita",
                $"¿Eliminar la actividad de {fila.Original.Responsable} " +
                $"a las {fila.Original.Hora}?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        try
        {
            await Task.Run(() =>
                DatabaseHelper.EliminarActividadProgramaVisitaMobile(
                    fila.Original.Id));

            await RecargarProgramaAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Programa de la visita",
                "No se pudo eliminar la actividad:\n\n" + ex.Message,
                "Aceptar");
        }
    }


    private async Task RecargarProgramaAsync()
    {
        if (_vista is null)
        {
            return;
        }

        List<VistaProgramaVisitaMobile> programa =
            await Task.Run(() =>
                DatabaseHelper.ObtenerProgramaVisitaEditableMobile(
                    _vista.CongregacionCodigo,
                    _vista.MesReferencia));

        _vista.Programa = programa;
        PrepararPrograma();

        BtnPrograma.Text =
            $"Programa de la visita  ({_programaUi.Count})";

        if (PanelPrograma.IsVisible)
        {
            PintarPrograma();
        }
    }


    // ========================================================================
    // NOTAS PERSONALES
    // ========================================================================

    private int ContarNotasConContenido()
    {
        if (_vista is null)
        {
            return 0;
        }

        return _vista.Notas.Count(
            n =>
                !string.IsNullOrWhiteSpace(n.Respuesta)
                ||
                n.EsAlerta);
    }


    private async void BtnNotas_Clicked(
        object sender,
        EventArgs e)
    {
        PanelNotasContenedor.IsVisible =
            !PanelNotasContenedor.IsVisible;

        if (!PanelNotasContenedor.IsVisible)
        {
            return;
        }

        await PrepararNotasAsync();
    }


    private async Task PrepararNotasAsync()
    {
        if (_vista is null)
        {
            return;
        }

        if (!_notasPreparadas)
        {
            if (_vista.SemanaId <= 0)
            {
                LblSinNotas.Text =
                    "No se ha encontrado la semana de visita en el calendario.";
                LblSinNotas.IsVisible = true;
                return;
            }

            _vista.Notas =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerOCrearNotasVisitaMobile(
                        _vista.SemanaId));

            List<string> categorias =
                _vista.Notas
                    .Select(n => n.Cabecera)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();

            PickerCategoriaNotas.ItemsSource = categorias;

            if (categorias.Count > 0)
            {
                PickerCategoriaNotas.SelectedIndex = 0;
            }

            _notasPreparadas = true;
        }

        PintarNotasCategoria();
    }


    private void PickerCategoriaNotas_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        if (_notasPreparadas)
        {
            PintarNotasCategoria();
        }
    }


    private void PintarNotasCategoria()
    {
        PanelNotas.Children.Clear();

        if (_vista is null)
        {
            return;
        }

        string categoria =
            PickerCategoriaNotas.SelectedItem as string
            ?? "";

        List<VistaNotaVisitaMobile> notas =
            _vista.Notas
                .Where(n =>
                    string.IsNullOrWhiteSpace(categoria)
                    ||
                    string.Equals(
                        n.Cabecera,
                        categoria,
                        StringComparison.Ordinal))
                .ToList();

        LblSinNotas.IsVisible = notas.Count == 0;

        foreach (VistaNotaVisitaMobile nota in notas)
        {
            var pregunta =
                new Label
                {
                    Text = nota.Pregunta,
                    TextColor = Color.FromArgb("#B6BBC2"),
                    FontSize = 11,
                    LineBreakMode = LineBreakMode.WordWrap
                };

            var editor =
                new Editor
                {
                    Text = nota.Respuesta,
                    Placeholder = "Escribe aquí...",
                    PlaceholderColor = Color.FromArgb("#646B74"),
                    TextColor = Colors.White,
                    BackgroundColor = Color.FromArgb("#1A1D22"),
                    MinimumHeightRequest = 72,
                    AutoSize = EditorAutoSizeOption.TextChanges
                };

            editor.TextChanged +=
                (_, args) =>
                {
                    nota.Respuesta = args.NewTextValue ?? "";
                    ProgramarGuardadoNota(nota);
                };

            editor.Unfocused +=
                async (_, _) =>
                    await GuardarNotaAhoraAsync(nota);

            var btnAlerta =
                new Button
                {
                    Text = nota.EsAlerta
                        ? "🚩 Marcada"
                        : "🚩 Marcar alerta",
                    BackgroundColor = nota.EsAlerta
                        ? Color.FromArgb("#4B2227")
                        : Color.FromArgb("#242830"),
                    TextColor = nota.EsAlerta
                        ? Color.FromArgb("#FFD4D8")
                        : Color.FromArgb("#B6BBC2"),
                    FontSize = 10,
                    CornerRadius = 8,
                    Padding = new Thickness(8, 4)
                };

            btnAlerta.Clicked +=
                async (_, _) =>
                {
                    nota.EsAlerta = !nota.EsAlerta;
                    btnAlerta.Text = nota.EsAlerta
                        ? "🚩 Marcada"
                        : "🚩 Marcar alerta";
                    btnAlerta.BackgroundColor = nota.EsAlerta
                        ? Color.FromArgb("#4B2227")
                        : Color.FromArgb("#242830");
                    btnAlerta.TextColor = nota.EsAlerta
                        ? Color.FromArgb("#FFD4D8")
                        : Color.FromArgb("#B6BBC2");

                    await GuardarNotaAhoraAsync(nota);
                };

            var contenido =
                new VerticalStackLayout
                {
                    Spacing = 7,
                    Children =
                    {
                        pregunta,
                        editor,
                        btnAlerta
                    }
                };

            PanelNotas.Children.Add(
                new Border
                {
                    BackgroundColor = Color.FromArgb("#171A1F"),
                    Stroke = Color.FromArgb("#343942"),
                    StrokeThickness = 1,
                    Padding = 11,
                    StrokeShape =
                        new Microsoft.Maui.Controls.Shapes.RoundRectangle
                        {
                            CornerRadius = new CornerRadius(10)
                        },
                    Content = contenido
                });
        }
    }


    private void ProgramarGuardadoNota(
        VistaNotaVisitaMobile nota)
    {
        if (nota.Id <= 0)
        {
            return;
        }

        if (_guardadosNotaPendientes.TryGetValue(
                nota.Id,
                out CancellationTokenSource? anterior))
        {
            anterior.Cancel();
            anterior.Dispose();
        }

        var cts = new CancellationTokenSource();
        _guardadosNotaPendientes[nota.Id] = cts;

        _ = GuardarNotaDebounceAsync(
            nota,
            cts);
    }


    private async Task GuardarNotaDebounceAsync(
        VistaNotaVisitaMobile nota,
        CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(700, cts.Token);

            await Task.Run(
                () =>
                    DatabaseHelper.ActualizarNotaVisitaMobile(nota),
                cts.Token);

            MainThread.BeginInvokeOnMainThread(
                () =>
                {
                    BtnNotas.Text =
                        $"Notas personales de la visita  ({ContarNotasConContenido()})";
                });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // El guardado al perder foco volverá a intentarlo.
        }
    }


    private async Task GuardarNotaAhoraAsync(
        VistaNotaVisitaMobile nota)
    {
        if (_guardadosNotaPendientes.TryGetValue(
                nota.Id,
                out CancellationTokenSource? pendiente))
        {
            pendiente.Cancel();
            pendiente.Dispose();
            _guardadosNotaPendientes.Remove(nota.Id);
        }

        try
        {
            await Task.Run(() =>
                DatabaseHelper.ActualizarNotaVisitaMobile(nota));

            BtnNotas.Text =
                $"Notas personales de la visita  ({ContarNotasConContenido()})";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Notas de la visita",
                "No se pudo guardar la nota:\n\n" + ex.Message,
                "Aceptar");
        }
    }


    // ========================================================================
    // TARJETAS S-21
    // ========================================================================

    private async void BtnMapa_Clicked(
        object sender,
        EventArgs e)
    {
        PanelMapa.IsVisible =
            !PanelMapa.IsVisible;

        if (!PanelMapa.IsVisible)
        {
            return;
        }

        await CargarMapaVisitaAsync();
    }


    /// <summary>
    /// Mapa de la congregación visitada, igual que la pestaña "Mapa" de
    /// Windows: geocodifica las direcciones pendientes (Nominatim),
    /// guarda las coordenadas en Personas.Latitud/Longitud y dibuja un
    /// mapa Leaflet con un marcador por publicador y otro rojo para el
    /// Salón del Reino.
    /// </summary>
    private async Task CargarMapaVisitaAsync()
    {
        if (_vista is null)
        {
            return;
        }

        string codigo =
            _vista.CongregacionCodigo ?? "";

        if (string.IsNullOrWhiteSpace(codigo))
        {
            LblMapaEstado.Text =
                "No se pudo identificar la congregación.";

            return;
        }

        try
        {
            LblMapaEstado.Text =
                "Preparando el mapa…";

            List<Persona> personas =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerPersonas()
                            .Where(p =>
                                string.Equals(
                                    p.CongregacionCodigo,
                                    codigo,
                                    StringComparison.OrdinalIgnoreCase))
                            .ToList());

            Congregacion? congregacion =
                (await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerCongregacionesCompletasMobile()))
                .FirstOrDefault(c =>
                    string.Equals(
                        c.Codigo,
                        codigo,
                        StringComparison.OrdinalIgnoreCase));

            // 1. Pintamos el mapa con lo que ya está geocodificado.
            DibujarMapa(
                personas,
                congregacion);

            // 2. Geocodificamos las direcciones que aún no tienen
            //    coordenadas (con un límite por visita para no eternizar).
            List<Persona> pendientes =
                personas
                    .Where(p =>
                        !string.IsNullOrWhiteSpace(
                            p.Direccion)
                        && p.Latitud == 0
                        && p.Longitud == 0)
                    .Take(40)
                    .ToList();

            if (pendientes.Count == 0)
            {
                LblMapaEstado.Text =
                    $"{personas.Count} publicadores · " +
                    "todas las direcciones están geocodificadas.";

                return;
            }

            int hechas = 0;

            foreach (Persona persona in pendientes)
            {
                LblMapaEstado.Text =
                    $"Geolocalizando direcciones… " +
                    $"({hechas + 1}/{pendientes.Count}): " +
                    persona.NombreMostrarTabla;

                GeocodificacionService
                    .ResultadoGeocodificacion resultado =
                        await GeocodificacionService
                            .BuscarAsync(
                                persona.Direccion);

                double latitud =
                    resultado.Ok
                        ? resultado.Latitud
                        : 999;

                double longitud =
                    resultado.Ok
                        ? resultado.Longitud
                        : 999;

                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ActualizarCoordenadasPersonaMobile(
                                persona.Id,
                                latitud,
                                longitud));

                persona.Latitud =
                    latitud;

                persona.Longitud =
                    longitud;

                hechas++;

                await Task.Delay(
                    1200);
            }

            DibujarMapa(
                personas,
                congregacion);

            int conPunto =
                personas.Count(p =>
                    p.Latitud != 0
                    && p.Latitud != 999);

            LblMapaEstado.Text =
                $"{personas.Count} publicadores · " +
                $"{conPunto} geolocalizados.";
        }
        catch (Exception ex)
        {
            LblMapaEstado.Text =
                "No se pudo preparar el mapa: " +
                ex.Message;
        }
    }


    private void DibujarMapa(
        List<Persona> personas,
        Congregacion? congregacion)
    {
        var puntos =
            personas
                .Where(p =>
                    p.Latitud != 0
                    && p.Latitud != 999
                    && p.Longitud != 0
                    && p.Longitud != 999)
                .Select(p => new
                {
                    nombre = p.NombreMostrarTabla,
                    direccion = p.Direccion ?? "",
                    lat = p.Latitud,
                    lon = p.Longitud
                })
                .ToList();

        string jsonPuntos =
            System.Text.Json.JsonSerializer.Serialize(
                puntos);

        double latCentro =
            congregacion is not null
            && congregacion.Latitud != 0
                ? congregacion.Latitud
                : (puntos.Count > 0
                    ? puntos[0].lat
                    : 39.5);

        double lonCentro =
            congregacion is not null
            && congregacion.Longitud != 0
                ? congregacion.Longitud
                : (puntos.Count > 0
                    ? puntos[0].lon
                    : -3.7);

        string latSR =
            (congregacion?.Latitud ?? 0)
                .ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

        string lonSR =
            (congregacion?.Longitud ?? 0)
                .ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

        string nombreSR =
            System.Text.Json.JsonSerializer.Serialize(
                congregacion?.Nombre ?? "Salón del Reino");

        string direccionSR =
            System.Text.Json.JsonSerializer.Serialize(
                congregacion?.DireccionSR ?? "");

        string latCentroTexto =
            latCentro.ToString(
                System.Globalization.CultureInfo.InvariantCulture);

        string lonCentroTexto =
            lonCentro.ToString(
                System.Globalization.CultureInfo.InvariantCulture);

        string html =
            $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="UTF-8" />
            <meta name="viewport"
                  content="width=device-width, initial-scale=1" />
            <link rel="stylesheet"
                  href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
            <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
            <style>
            html, body { margin:0; padding:0; height:100%; background:#101216; }
            #mapa { position:absolute; inset:0; }
            </style>
            </head>
            <body>
            <div id="mapa"></div>
            <script>
            var map = L.map('mapa').setView([{{latCentroTexto}}, {{lonCentroTexto}}], 13);
            L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; OpenStreetMap'
            }).addTo(map);

            var puntos = {{jsonPuntos}};
            var limites = [];

            for (var i = 0; i < puntos.length; i++) {
                var p = puntos[i];
                var m = L.marker([p.lat, p.lon]).addTo(map)
                    .bindPopup('<b>' + p.nombre + '</b><br>' + p.direccion);
                limites.push([p.lat, p.lon]);
            }

            if ({{latSR}} !== 0 && {{lonSR}} !== 0) {
                var srIcon = new L.Icon({
                    iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-red.png',
                    shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
                    iconSize: [25, 41],
                    iconAnchor: [12, 41],
                    popupAnchor: [1, -34],
                    shadowSize: [41, 41]
                });

                L.marker([{{latSR}}, {{lonSR}}], { icon: srIcon }).addTo(map)
                    .bindPopup('<b style="color:red">SALÓN DEL REINO</b><br>'
                               + {{nombreSR}} + '<br>' + {{direccionSR}});

                limites.push([{{latSR}}, {{lonSR}}]);
            }

            if (limites.length > 0) {
                map.fitBounds(limites, { padding: [25, 25] });
            }
            </script>
            </body>
            </html>
            """;

        MapaWebView.Source =
            new HtmlWebViewSource
            {
                Html = html
            };
    }


    // ========================================================================
    // MAPA DEL SALÓN DEL REINO
    // ========================================================================

    private async void BtnMapaSalon_Clicked(
        object sender,
        EventArgs e)
    {
        if (_vista is null)
        {
            return;
        }

        try
        {
            double lat =
                _vista.LatitudSalon;

            double lon =
                _vista.LongitudSalon;

            if (lat == 0
                &&
                lon == 0)
            {
                // Intentamos geocodificar la dirección del Salón.
                string consulta =
                    string.Join(
                        ", ",
                        new[]
                        {
                            _vista.DireccionSalon,
                            _vista.Municipio,
                            _vista.Provincia
                        }
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(
                                x)));

                if (string.IsNullOrWhiteSpace(
                        consulta))
                {
                    await DisplayAlert(
                        "Mapa",
                        "La congregación no tiene dirección ni " +
                        "coordenadas del Salón registradas.",
                        "Aceptar");

                    return;
                }

                GeocodificacionService.ResultadoGeocodificacion
                    resultado =
                        await GeocodificacionService
                            .BuscarAsync(
                                consulta);

                if (!resultado.Ok)
                {
                    // Aun sin coordenadas exactas, abrimos la búsqueda
                    // por dirección en el mapa del dispositivo.
                    await Launcher.Default.OpenAsync(
                        new Uri(
                            "https://www.google.com/maps/search/?api=1" +
                            "&query=" +
                            Uri.EscapeDataString(
                                consulta)));

                    return;
                }

                lat =
                    resultado.Latitud;

                lon =
                    resultado.Longitud;
            }

            string latTexto =
                lat.ToString(
                    CultureInfo.InvariantCulture);

            string lonTexto =
                lon.ToString(
                    CultureInfo.InvariantCulture);

            await Launcher.Default.OpenAsync(
                new Uri(
                    "https://www.google.com/maps/search/?api=1" +
                    $"&query={latTexto},{lonTexto}"));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Mapa",
                "No se pudo abrir el mapa: " +
                ex.Message,
                "Aceptar");
        }
    }


    // ========================================================================
    // TERRITORIOS
    // ========================================================================

    private void PrepararTerritorios()
    {
        if (_vista is null)
        {
            return;
        }

        _territoriosUi =
            _vista.Territorios
                .Select(t =>
                    new TerritorioUi
                    {
                        Original = t,
                        Nombre = t.Nombre,
                        Estado = CrearEstadoTerritorio(t),
                        Ultima =
                            string.IsNullOrWhiteSpace(
                                t.UltimaFechaCompletado)
                                ? ""
                                : "Última vez completado: " +
                                  t.UltimaFechaCompletado
                    })
                .ToList();

        _territoriosPreparados = false;
    }


    private void PintarTerritorios()
    {
        _territoriosPreparados = true;

        int total =
            _territoriosUi.Count;

        int paginas =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    total
                    /
                    (double)TerritoriosPorPagina));

        if (_paginaTerritorios > paginas - 1)
        {
            _paginaTerritorios =
                paginas - 1;
        }

        if (_paginaTerritorios < 0)
        {
            _paginaTerritorios = 0;
        }

        ListaTerritorios.ItemsSource =
            _territoriosUi
                .Skip(
                    _paginaTerritorios
                    *
                    TerritoriosPorPagina)
                .Take(TerritoriosPorPagina)
                .ToList();

        LblTotalTerritorios.Text =
            $"{total} territorios";

        int desde =
            total == 0
                ? 0
                : _paginaTerritorios
                  *
                  TerritoriosPorPagina
                  + 1;

        int hasta =
            Math.Min(
                total,
                (_paginaTerritorios + 1)
                *
                TerritoriosPorPagina);

        LblPaginaTerritorios.Text =
            $"{desde}–{hasta} de {total}";

        BtnTerrAnt.IsEnabled =
            _paginaTerritorios > 0;

        BtnTerrSig.IsEnabled =
            _paginaTerritorios < paginas - 1;
    }


    private void BtnTerrAnt_Clicked(
        object sender,
        EventArgs e)
    {
        if (_paginaTerritorios <= 0)
        {
            return;
        }

        _paginaTerritorios--;

        PintarTerritorios();
    }


    private void BtnTerrSig_Clicked(
        object sender,
        EventArgs e)
    {
        int total =
            _territoriosUi.Count;

        int ultimaPagina =
            Math.Max(
                0,
                (int)Math.Ceiling(
                    total
                    /
                    (double)TerritoriosPorPagina)
                - 1);

        if (_paginaTerritorios >= ultimaPagina)
        {
            return;
        }

        _paginaTerritorios++;

        PintarTerritorios();
    }


    private void BtnTerritorios_Clicked(
        object sender,
        EventArgs e)
    {
        PanelTerritorios.IsVisible =
            !PanelTerritorios.IsVisible;

        if (PanelTerritorios.IsVisible
            &&
            !_territoriosPreparados)
        {
            PintarTerritorios();
        }
    }


    private async void BtnImportarTerritoriosJson_Clicked(
        object sender,
        EventArgs e)
    {
        if (_vista is null)
        {
            return;
        }

        try
        {
            FileResult? archivo =
                await SeleccionarJsonTerritoriosAsync();

            if (archivo is null)
            {
                return;
            }

            string contenido =
                (await LeerArchivoTextoAsync(
                    archivo))
                .Trim();

            if (!contenido.StartsWith(
                    "[",
                    StringComparison.Ordinal))
            {
                contenido =
                    "[" + contenido + "]";
            }

            List<TerritorioJsonMobile> territorios =
                JsonSerializer
                    .Deserialize<List<TerritorioJsonMobile>>(
                        contenido,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true,

                            AllowTrailingCommas =
                                true,

                            ReadCommentHandling =
                                JsonCommentHandling.Skip
                        })
                ?? new();

            if (territorios.Count == 0)
            {
                await DisplayAlert(
                    "Territorios",
                    "El archivo no contiene territorios.",
                    "Aceptar");

                return;
            }

            int existentes =
                _vista.Territorios.Count;

            if (existentes > 0)
            {
                bool continuar =
                    await DisplayAlert(
                        "Importar S-13",
                        "Esta congregación ya tiene " +
                        existentes + " territorios. " +
                        "Se sustituirán por los " +
                        territorios.Count +
                        " del archivo.\n\n¿Continuar?",
                        "Sí, importar",
                        "Cancelar");

                if (!continuar)
                {
                    return;
                }
            }

            string codigo =
                _vista.CongregacionCodigo;

            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarTerritoriosImportadosMobile(
                            codigo,
                            territorios));

            await RecargarTerritoriosAsync();

            await DisplayAlert(
                "Territorios",
                "Se importaron " +
                territorios.Count +
                " territorios del S-13.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Territorios",
                "No se pudo importar el JSON:\n\n" +
                ex.Message,
                "Aceptar");
        }
    }


    private static async Task<FileResult?>
        SeleccionarJsonTerritoriosAsync()
    {
        try
        {
            var tipos =
                new FilePickerFileType(
                    new Dictionary<
                        DevicePlatform,
                        IEnumerable<string>>
                    {
                        [DevicePlatform.Android] =
                            new[]
                            {
                                "application/json",
                                "text/json",
                                "text/plain",
                                "application/octet-stream"
                            }
                    });

            return await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle =
                        "Seleccionar S-13 (territorios)",

                    FileTypes =
                        tipos
                });
        }
        catch
        {
            return await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle =
                        "Seleccionar S-13 (territorios)"
                });
        }
    }


    private static async Task<string>
        LeerArchivoTextoAsync(
            FileResult archivo)
    {
        await using Stream stream =
            await archivo.OpenReadAsync();

        using var reader =
            new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }


    private async void Territorio_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not
                TerritorioUi territorio
            ||
            _vista is null)
        {
            return;
        }

        var editor =
            new TerritorioEdicionPage(
                territorio.Original,
                _vista.CongregacionCodigo);

        await Navigation.PushAsync(editor);

        bool cambiado =
            await editor.EsperarResultadoAsync();

        if (cambiado)
        {
            await RecargarTerritoriosAsync();
        }
    }


    private async Task RecargarTerritoriosAsync()
    {
        if (_vista is null)
        {
            return;
        }

        try
        {
            _vista =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerVistaVisitaMobile(
                                _semana.Congregacion ?? "",
                                _semana.FechaMartes));

            PrepararTerritorios();

            PintarTerritorios();

            BtnTerritorios.Text =
                $"Territorios (S-13)  ({_territoriosUi.Count})";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Territorios",
                "No se pudieron recargar los territorios: " +
                ex.Message,
                "Aceptar");
        }
    }


    private static string CrearEstadoTerritorio(
        VistaTerritorioMobile t)
    {
        if (!string.IsNullOrWhiteSpace(t.FechaCompletado))
        {
            return "Completado: " + t.FechaCompletado;
        }

        if (!string.IsNullOrWhiteSpace(t.AsignadoA)
            ||
            !string.IsNullOrWhiteSpace(t.NombreVinculado))
        {
            string persona =
                !string.IsNullOrWhiteSpace(t.NombreVinculado)
                    ? t.NombreVinculado
                    : t.AsignadoA;

            return "Asignado a " +
                   persona +
                   (string.IsNullOrWhiteSpace(t.FechaAsignado)
                       ? ""
                       : " · " + t.FechaAsignado);
        }

        return "Disponible";
    }


    // ========================================================================
    // CONTABILIDAD
    // ========================================================================

    private async void BtnContabilidad_Clicked(
        object sender,
        EventArgs e)
    {
        PanelContabilidad.IsVisible =
            !PanelContabilidad.IsVisible;

        if (PanelContabilidad.IsVisible)
        {
            await CargarContabilidadEditableAsync();
        }
    }


    private async void BtnMesAntContabilidad_Clicked(
        object sender,
        EventArgs e)
    {
        _mesContabilidad = _mesContabilidad.AddMonths(-1);
        await CargarContabilidadEditableAsync();
    }


    private async void BtnMesSigContabilidad_Clicked(
        object sender,
        EventArgs e)
    {
        _mesContabilidad = _mesContabilidad.AddMonths(1);
        await CargarContabilidadEditableAsync();
    }


    private async Task CargarContabilidadEditableAsync()
    {
        if (_vista is null)
        {
            return;
        }

        LblMesContabilidad.Text =
            "Hasta " + FormatearMesCompleto(_mesContabilidad);

        List<VistaContabilidadMobile> filas =
            await Task.Run(() =>
                DatabaseHelper.ObtenerContabilidadVisitaEditableMobile(
                    _vista.CongregacionCodigo,
                    _mesContabilidad));

        PanelContabilidadFilas.Children.Clear();

        foreach (VistaContabilidadMobile fila in filas)
        {
            PanelContabilidadFilas.Children.Add(
                CrearTarjetaContabilidad(fila));
        }
    }


    private View CrearTarjetaContabilidad(
        VistaContabilidadMobile fila)
    {
        var panel =
            new VerticalStackLayout
            {
                Spacing = 7
            };

        panel.Children.Add(
            new Label
            {
                Text = Capitalizar(fila.EtiquetaMes),
                TextColor = Colors.White,
                FontAttributes = FontAttributes.Bold,
                FontSize = 14
            });

        var lblPromedio =
            new Label
            {
                TextColor = Color.FromArgb("#FFD76B"),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold
            };

        Entry gastos = CrearEntradaMoneda(fila.Gastos);
        Entry mantenimiento = CrearEntradaMoneda(fila.Mantenimiento);
        Entry obraMundial = CrearEntradaMoneda(fila.ObraMundial);
        Entry obraResolucion = CrearEntradaMoneda(fila.ObraResolucion);
        Entry saldo = CrearEntradaMoneda(fila.Saldo);

        panel.Children.Add(CrearFilaContable("Gastos", gastos));
        panel.Children.Add(CrearFilaContable("Mantenimiento", mantenimiento));
        panel.Children.Add(CrearFilaContable("Obra mundial", obraMundial));
        panel.Children.Add(CrearFilaContable("Resolución", obraResolucion));
        panel.Children.Add(CrearFilaContable("Saldo", saldo));
        panel.Children.Add(lblPromedio);

        void RefrescarModeloYPromedio()
        {
            fila.Gastos = ParsearMoneda(gastos.Text);
            fila.Mantenimiento = ParsearMoneda(mantenimiento.Text);
            fila.ObraMundial = ParsearMoneda(obraMundial.Text);
            fila.ObraResolucion = ParsearMoneda(obraResolucion.Text);
            fila.Saldo = ParsearMoneda(saldo.Text);
            fila.TieneDatos =
                fila.Gastos != 0
                || fila.Mantenimiento != 0
                || fila.ObraMundial != 0
                || fila.ObraResolucion != 0
                || fila.Saldo != 0;

            double suma =
                fila.Gastos +
                fila.Mantenimiento +
                fila.ObraMundial +
                fila.ObraResolucion;

            int publicadores = _vista?.TotalPersonas ?? 0;

            lblPromedio.Text =
                publicadores > 0
                    ? $"Promedio por publicador: {(suma / publicadores):0.00} €"
                    : "Promedio por publicador: 0.00 €";
        }

        RefrescarModeloYPromedio();

        foreach (Entry entry in new[]
                 {
                     gastos,
                     mantenimiento,
                     obraMundial,
                     obraResolucion,
                     saldo
                 })
        {
            entry.TextChanged += (_, _) =>
                RefrescarModeloYPromedio();

            entry.Unfocused +=
                async (_, _) =>
                {
                    RefrescarModeloYPromedio();

                    try
                    {
                        await Task.Run(() =>
                            DatabaseHelper.GuardarContabilidadVisitaMobile(
                                _vista!.CongregacionCodigo,
                                fila));
                    }
                    catch (Exception ex)
                    {
                        await DisplayAlert(
                            "Contabilidad",
                            "No se pudo guardar la contabilidad:\n\n" +
                            ex.Message,
                            "Aceptar");
                    }
                };
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#171A1F"),
            Stroke = Color.FromArgb("#343942"),
            StrokeThickness = 1,
            Padding = 11,
            StrokeShape =
                new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = new CornerRadius(10)
                },
            Content = panel
        };
    }


    private static Grid CrearFilaContable(
        string titulo,
        Entry entry)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(130))
                },
                ColumnSpacing = 8
            };

        grid.Add(
            new Label
            {
                Text = titulo,
                TextColor = Color.FromArgb("#B6BBC2"),
                FontSize = 11,
                VerticalTextAlignment = TextAlignment.Center
            },
            0,
            0);

        grid.Add(entry, 1, 0);

        return grid;
    }


    private static Entry CrearEntradaMoneda(
        double valor)
    {
        return new Entry
        {
            Text = valor == 0
                ? ""
                : valor.ToString(
                    "0.00",
                    CultureInfo.CurrentCulture),
            Keyboard = Keyboard.Numeric,
            HorizontalTextAlignment = TextAlignment.End,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#1A1D22"),
            Placeholder = "0,00",
            PlaceholderColor = Color.FromArgb("#646B74")
        };
    }


    private static double ParsearMoneda(
        string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return 0;
        }

        string limpio =
            texto
                .Replace("€", "")
                .Replace(" ", "")
                .Trim();

        if (double.TryParse(
                limpio,
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                out double valorLocal))
        {
            return valorLocal;
        }

        if (double.TryParse(
                limpio.Replace(",", "."),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double valorInvariant))
        {
            return valorInvariant;
        }

        return 0;
    }


    // ========================================================================
    // S-303 / S-2
    // ========================================================================

    private async void BtnS303_Clicked(
        object sender,
        EventArgs e)
    {
        PanelS303.IsVisible = !PanelS303.IsVisible;

        if (PanelS303.IsVisible)
        {
            await CargarS303yS2Async();
        }
    }


    private async Task CargarS303yS2Async()
    {
        if (_vista is null)
        {
            return;
        }

        _documentoS303 =
            await Task.Run(() =>
                DatabaseHelper.ObtenerS303VisitaMobile(
                    _vista.CongregacionCodigo,
                    _vista.MesReferencia));

        _plantillaS303 =
            await Task.Run(() =>
                DatabaseHelper.ObtenerPlantillaS303VisitaMobile());

        _s2Ui =
            (await Task.Run(() =>
                DatabaseHelper.ObtenerRecomendacionesS2VisitaMobile(
                    _vista.CongregacionNombre,
                    _vista.CongregacionCodigo)))
            .Select(r =>
                new RecomendacionS2Ui
                {
                    Original = r
                })
            .ToList();

        PintarEstadoS303();
        PintarS2();

        if (!_s303Preparado)
        {
            string asunto =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                        "Asunto_Envío de S-303",
                        "Informe S-303"));

            string cuerpo =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                        "Email_Envío de S-303",
                        "Queridos hermanos,<br><br>Adjunto el informe S-303 de la visita.<br><br>Un saludo cariñoso."));

            TxtAsuntoS303.Text =
                asunto.Replace(
                    "[Congregacion]",
                    _vista.CongregacionNombre);

            string cuerpoHtml =
                cuerpo.Replace(
                    "[Congregacion]",
                    _vista.CongregacionNombre);

            TxtCuerpoS303.Text =
                HtmlATextoPlano(
                    cuerpoHtml);

            _s303Preparado = true;
        }
    }


    private void PintarEstadoS303()
    {
        if (_documentoS303 is not null)
        {
            LblEstadoS303.Text =
                "S-303 de esta visita disponible: " +
                _documentoS303.NombreArchivo;
            return;
        }

        if (_plantillaS303 is not null)
        {
            LblEstadoS303.Text =
                "Plantilla detectada: " +
                _plantillaS303.NombreArchivo;
            return;
        }

        LblEstadoS303.Text =
            "Todavía no hay una plantilla S-303. Pulsa «Plantilla PDF» para seleccionarla.";
    }


    private void PintarS2()
    {
        PanelS2.Children.Clear();
        LblSinS2.IsVisible = _s2Ui.Count == 0;

        foreach (RecomendacionS2Ui item in _s2Ui)
        {
            // Selector Sí/No, igual que Windows: Sí = aprobar en JW Hub;
            // No = cancelar en JW Hub al enviar.
            var seleccion =
                new Picker
                {
                    ItemsSource =
                        new List<string> { "Sí", "No" },
                    SelectedItem = item.Decision,
                    WidthRequest = 78,
                    FontSize = 12,
                    BackgroundColor = Color.FromArgb("#242830"),
                    TextColor = Colors.White
                };

            seleccion.SelectedIndexChanged +=
                (_, _) =>
                {
                    if (seleccion.SelectedItem is string decision)
                    {
                        item.Decision = decision;
                    }
                };

            var selector =
                new VerticalStackLayout
                {
                    Spacing = 1,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = "¿Aprobar?",
                            TextColor = Color.FromArgb("#9EA4AE"),
                            FontSize = 9
                        },
                        seleccion
                    }
                };

            var texto =
                new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = item.Original.Hermano,
                            TextColor = Colors.White,
                            FontAttributes = FontAttributes.Bold,
                            FontSize = 12
                        },
                        new Label
                        {
                            Text = string.Join(
                                " · ",
                                new[]
                                {
                                    item.Original.Tipo,
                                    item.Original.RecomendadoComo
                                }
                                .Where(x => !string.IsNullOrWhiteSpace(x))),
                            TextColor = Color.FromArgb("#B6BBC2"),
                            FontSize = 10
                        }
                    }
                };

            var grid =
                new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star)
                    },
                    ColumnSpacing = 8
                };

            grid.Add(selector, 0, 0);
            grid.Add(texto, 1, 0);

            PanelS2.Children.Add(
                new Border
                {
                    BackgroundColor = Color.FromArgb("#1A1D22"),
                    Stroke = Color.FromArgb("#343942"),
                    StrokeThickness = 1,
                    Padding = 8,
                    StrokeShape =
                        new Microsoft.Maui.Controls.Shapes.RoundRectangle
                        {
                            CornerRadius = new CornerRadius(8)
                        },
                    Content = grid
                });
        }
    }


    private async void BtnSeleccionarPlantillaS303_Clicked(
        object sender,
        EventArgs e)
    {
        FileResult? resultado =
            await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle = "Selecciona la plantilla S-303 en PDF"
                });

        if (resultado is null)
        {
            return;
        }

        try
        {
            byte[] contenido =
                await LeerArchivoSeleccionadoAsync(resultado);

            ValidarPdf(contenido);

            await Task.Run(() =>
                DatabaseHelper.GuardarPlantillaS303VisitaMobile(
                    resultado.FileName,
                    resultado.FullPath ?? "",
                    contenido));

            await CargarS303yS2Async();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "S-303",
                "No se pudo guardar la plantilla:\n\n" + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnCrearAbrirS303_Clicked(
        object sender,
        EventArgs e)
    {
        if (_vista is null)
        {
            return;
        }

        try
        {
            if (_documentoS303 is not null)
            {
                string existente =
                    await MaterializarDocumentoAsync(
                        _documentoS303,
                        _documentoS303.NombreArchivo);

                await AbrirPdfAsync(existente);
                return;
            }

            if (_plantillaS303 is null
                ||
                _plantillaS303.Contenido.Length == 0)
            {
                await DisplayAlert(
                    "S-303",
                    "Primero selecciona la plantilla PDF del S-303.",
                    "Aceptar");
                return;
            }

            MostrarCarga("Creando S-303...");

            string nombreSC =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                        "NombreSC",
                        ""));

            string apellidosSC =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                        "ApellidosSC",
                        ""));

            if (string.IsNullOrWhiteSpace(apellidosSC))
            {
                apellidosSC =
                    await Task.Run(() =>
                        DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                            "ApellidoSC",
                            ""));
            }

            string circuito =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                        "NumeroCircuito",
                        ""));

            var datos =
                new DatosS303Mobile
                {
                    CodigoCongregacion = _vista.CongregacionCodigo,
                    FechaVisita = CrearTextoFechaVisita(_semana.FechaMartes),
                    NombreCongregacion = _vista.CongregacionNombre,
                    Municipio = _vista.Municipio,
                    Provincia = _vista.Provincia,
                    NombreSuperintendente =
                        $"{nombreSC} {apellidosSC}".Trim(),
                    NumeroCircuito = circuito
                };

            byte[] pdf =
                await Task.Run(() =>
                    S303MobileService.CrearDesdePlantilla(
                        _plantillaS303.Contenido,
                        datos));

            string nombreArchivo =
                $"S-303_{_vista.CongregacionCodigo}_{_vista.MesReferencia}.pdf";

            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    nombreArchivo);

            await File.WriteAllBytesAsync(ruta, pdf);

            await Task.Run(() =>
                DatabaseHelper.GuardarS303VisitaMobile(
                    _vista.CongregacionCodigo,
                    _vista.MesReferencia,
                    ruta,
                    pdf));

            await CargarS303yS2Async();
            await AbrirPdfAsync(ruta);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "S-303",
                "No se pudo crear o abrir el S-303:\n\n" + ex.Message,
                "Aceptar");
        }
        finally
        {
            OcultarCarga();
        }
    }


    private async void BtnImportarS303Completado_Clicked(
        object sender,
        EventArgs e)
    {
        if (_vista is null)
        {
            return;
        }

        FileResult? resultado =
            await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle = "Selecciona el S-303 completado"
                });

        if (resultado is null)
        {
            return;
        }

        try
        {
            byte[] contenido =
                await LeerArchivoSeleccionadoAsync(resultado);

            ValidarPdf(contenido);

            string nombreArchivo =
                $"S-303_{_vista.CongregacionCodigo}_{_vista.MesReferencia}.pdf";

            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    nombreArchivo);

            await File.WriteAllBytesAsync(ruta, contenido);

            await Task.Run(() =>
                DatabaseHelper.GuardarS303VisitaMobile(
                    _vista.CongregacionCodigo,
                    _vista.MesReferencia,
                    ruta,
                    contenido));

            await CargarS303yS2Async();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "S-303",
                "No se pudo importar el PDF:\n\n" + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnEliminarS303_Clicked(
        object sender,
        EventArgs e)
    {
        if (_documentoS303 is null)
        {
            await DisplayAlert(
                "S-303",
                "No hay un S-303 generado para esta visita.",
                "Aceptar");
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "S-303",
                "¿Eliminar el S-303 guardado de esta visita?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        await Task.Run(() =>
            DatabaseHelper.EliminarS303VisitaMobile(
                _documentoS303.Id));

        await CargarS303yS2Async();
    }


    private async void BtnEnviarS303_Clicked(
        object sender,
        EventArgs e)
    {
        if (_vista is null)
        {
            return;
        }

        if (_documentoS303 is null
            ||
            _documentoS303.Contenido.Length == 0)
        {
            await DisplayAlert(
                "S-303",
                "Primero crea o importa el S-303 de esta visita.",
                "Aceptar");
            return;
        }

        if (string.IsNullOrWhiteSpace(_vista.EmailCongregacion))
        {
            await DisplayAlert(
                "S-303",
                "La congregación no tiene una dirección de correo configurada.",
                "Aceptar");
            return;
        }

        List<RecomendacionS2Ui> aprobadas =
            _s2Ui.Where(x => x.Aprobar).ToList();

        List<RecomendacionS2Ui> canceladas =
            _s2Ui.Where(x => !x.Aprobar).ToList();

        if (_s2Ui.Count > 0)
        {
            bool continuar =
                await DisplayAlert(
                    "Confirmar recomendaciones y bajas",
                    $"Se van a procesar {_s2Ui.Count} recomendaciones de " +
                    $"{_vista.CongregacionNombre}:\n\n" +
                    $"  APROBAR: {aprobadas.Count}\n" +
                    $"  CANCELAR: {canceladas.Count}\n\n" +
                    "Las marcadas como «No» se cancelarán en JW Hub. " +
                    "Estos cambios pueden tener consecuencias reales.\n\n" +
                    "¿Confirmas las decisiones y el envío posterior del correo S-303?",
                    "Sí, continuar",
                    "Cancelar");

            if (!continuar)
            {
                return;
            }
        }

        string? rutaCarta = null;

        // La carta se prepara ANTES de tocar JW Hub: si fallara, no se
        // cambia ninguna recomendación ni se envía una carta incompleta.
        if (aprobadas.Count > 0)
        {
            try
            {
                rutaCarta =
                    await GenerarCartaNombramientosAsync(aprobadas);
            }
            catch (Exception exCarta)
            {
                await DisplayAlert(
                    "Carta de nombramientos",
                    "No se pudo preparar la carta en PDF. No se ha " +
                    "modificado JW Hub ni preparado el correo.\n\n" +
                    exCarta.Message,
                    "Aceptar");
                return;
            }
        }

        if (_s2Ui.Count > 0)
        {
#if ANDROID
            foreach (RecomendacionS2Ui item in _s2Ui)
            {
                if (string.IsNullOrWhiteSpace(item.Original.UrlDetalle))
                {
                    await DisplayAlert(
                        "JW Hub",
                        $"No se pudo abrir la recomendación de {item.Original.Hermano}.\n\n" +
                        "Sincroniza de nuevo con JW Hub y vuelve a intentarlo.",
                        "Aceptar");
                    return;
                }

                JwHubAccionesAndroid.ResultadoAccionHub resultado =
                    await JwHubAccionesAndroid.ProcesarRecomendacionAsync(
                        item.Original.UrlDetalle,
                        item.Aprobar,
                        DateTime.Today);

                if (!resultado.Correcto)
                {
                    await DisplayAlert(
                        "JW Hub",
                        $"No se pudo {(item.Aprobar ? "aprobar" : "cancelar")} a " +
                        $"{item.Original.Hermano}:\n\n{resultado.Mensaje}\n\n" +
                        "No se ha preparado el correo.",
                        "Aceptar");
                    return;
                }

                if (item.Original.Id > 0)
                {
                    int id = item.Original.Id;
                    await Task.Run(() =>
                        DatabaseHelper.EliminarRecomendacionNombramientoMobile(id));
                }
            }
#else
            await DisplayAlert(
                "JW Hub",
                "Aprobar o cancelar recomendaciones solo está disponible en Android.",
                "Aceptar");
            return;
#endif
        }

        try
        {
            string ruta =
                await MaterializarDocumentoAsync(
                    _documentoS303,
                    _documentoS303.NombreArchivo);

            string cuerpo =
                TextoPlanoAHtml(
                    TxtCuerpoS303.Text ?? "");

            if (aprobadas.Count > 0)
            {
                cuerpo +=
                    "<br><br><b>S-2 aprobados:</b><br>" +
                    string.Join(
                        "<br>",
                        aprobadas.Select(x =>
                            "• " +
                            WebUtility.HtmlEncode(x.Original.Hermano) +
                            " — " +
                            WebUtility.HtmlEncode(
                                string.Join(
                                    " / ",
                                    new[]
                                    {
                                        x.Original.Tipo,
                                        x.Original.RecomendadoComo
                                    }
                                    .Where(v => !string.IsNullOrWhiteSpace(v))))));
            }

            var mensaje =
                new EmailMessage
                {
                    Subject = TxtAsuntoS303.Text ?? "Informe S-303",
                    Body = cuerpo,
                    BodyFormat = EmailBodyFormat.Html
                };

            mensaje.To.Add(_vista.EmailCongregacion);

            if (ChkCopiaSRV.IsToggled)
            {
                string emailSrv =
                    await Task.Run(() =>
                        DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                            "EmailSRV",
                            "srv.es@jwpub.org"));

                if (!string.IsNullOrWhiteSpace(emailSrv))
                {
                    mensaje.Cc.Add(emailSrv);
                }
            }

            mensaje.Attachments.Add(
                new EmailAttachment(ruta));

            // Carta de nombramientos en PDF (solo si hay aprobaciones),
            // ya generada antes de tocar JW Hub.
            if (rutaCarta is not null)
            {
                mensaje.Attachments.Add(
                    new EmailAttachment(
                        rutaCarta));
            }

            await Email.Default.ComposeAsync(mensaje);
        }
        catch (FeatureNotSupportedException)
        {
            await DisplayAlert(
                "Correo",
                "No hay una aplicación de correo compatible configurada en el dispositivo.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Correo",
                "No se pudo preparar el correo:\n\n" + ex.Message,
                "Aceptar");
        }
    }


    /// <summary>
    /// Genera la carta de nombramientos (PDF) con las recomendaciones
    /// marcadas como «Sí», antes de aprobar/cancelar en JW Hub.
    /// </summary>
    private async Task<string> GenerarCartaNombramientosAsync(
        List<RecomendacionS2Ui> seleccionadas)
    {
        if (_vista is null)
        {
            throw new InvalidOperationException(
                "No hay datos de la visita.");
        }

        string datosCarta =
            await Task.Run(
                () =>
                {
                    string n =
                        DatabaseHelper
                            .ObtenerConfiguracionVisitaMobile(
                                "NombreSC",
                                "");

                    string a =
                        DatabaseHelper
                            .ObtenerConfiguracionVisitaMobile(
                                "ApellidoSC",
                                "");

                    if (string.IsNullOrWhiteSpace(
                            a))
                    {
                        a = DatabaseHelper
                            .ObtenerConfiguracionVisitaMobile(
                                "ApellidosSC",
                                "");
                    }

                    string c =
                        DatabaseHelper
                            .ObtenerConfiguracionVisitaMobile(
                                "NumeroCircuito",
                                "");

                    return (n + " " + a).Trim() +
                           "\u0001" +
                           c;
                });

        string[] partesCarta =
            datosCarta.Split('\u0001');

        byte[] carta =
            await Task.Run(
                () =>
                    CartaNombramientosPdfService
                        .Generar(
                            _vista.CongregacionNombre,
                            _vista.CongregacionCodigo,
                            partesCarta[0],
                            partesCarta.Length > 1
                                ? partesCarta[1]
                                : "",
                            DateTime.Today,
                            seleccionadas.Select(
                                x => x.Original)));

        string ruta =
            Path.Combine(
                FileSystem.CacheDirectory,
                "Carta_nombramientos_" +
                _vista.CongregacionCodigo +
                ".pdf");

        await File.WriteAllBytesAsync(
            ruta,
            carta);

        return ruta;
    }


    private static string HtmlATextoPlano(
        string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        string texto = html;

        texto = Regex.Replace(
            texto,
            @"<\s*br\s*/?\s*>",
            "\n",
            RegexOptions.IgnoreCase);

        texto = Regex.Replace(
            texto,
            @"<\s*li\b[^>]*>",
            "• ",
            RegexOptions.IgnoreCase);

        texto = Regex.Replace(
            texto,
            @"</\s*(p|div|li|h[1-6])\s*>",
            "\n",
            RegexOptions.IgnoreCase);

        texto = Regex.Replace(
            texto,
            @"<[^>]+>",
            "");

        texto =
            WebUtility
                .HtmlDecode(texto)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

        texto = Regex.Replace(
            texto,
            @"[ \t]+\n",
            "\n");

        texto = Regex.Replace(
            texto,
            @"\n{3,}",
            "\n\n");

        return texto.Trim();
    }


    private static string TextoPlanoAHtml(
        string? texto)
    {
        string limpio =
            (texto ?? "")
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

        return WebUtility
            .HtmlEncode(limpio)
            .Replace("\n", "<br>");
    }


    private static async Task<byte[]> LeerArchivoSeleccionadoAsync(
        FileResult resultado)
    {
        await using Stream stream =
            await resultado.OpenReadAsync();

        using var memoria = new MemoryStream();
        await stream.CopyToAsync(memoria);
        return memoria.ToArray();
    }


    private static void ValidarPdf(
        byte[] contenido)
    {
        if (contenido.Length < 4
            ||
            contenido[0] != (byte)'%'
            ||
            contenido[1] != (byte)'P'
            ||
            contenido[2] != (byte)'D'
            ||
            contenido[3] != (byte)'F')
        {
            throw new InvalidDataException(
                "El archivo seleccionado no parece ser un PDF válido.");
        }
    }


    private static async Task<string> MaterializarDocumentoAsync(
        DocumentoVisitaMobile documento,
        string nombreArchivo)
    {
        if (documento.Contenido.Length == 0)
        {
            throw new InvalidDataException(
                "El documento no contiene datos PDF.");
        }

        string nombreSeguro =
            string.IsNullOrWhiteSpace(nombreArchivo)
                ? "S-303.pdf"
                : nombreArchivo;

        string ruta =
            Path.Combine(
                FileSystem.CacheDirectory,
                nombreSeguro);

        await File.WriteAllBytesAsync(
            ruta,
            documento.Contenido);

        return ruta;
    }


    private static async Task AbrirPdfAsync(
        string ruta)
    {
        await Launcher.Default.OpenAsync(
            new OpenFileRequest(
                "S-303",
                new ReadOnlyFile(ruta)));
    }


    private static string CrearTextoFechaVisita(
        DateTime martes)
    {
        DateTime domingo = martes.AddDays(5);
        CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        return
            $"Del {martes.Day} de {martes.ToString("MMM", es)} " +
            $"al {domingo.Day} de {domingo.ToString("MMM", es)} " +
            $"de {domingo.Year}";
    }


    // ========================================================================
    // UTILIDADES
    // ========================================================================

    private static ImageSource? CrearFotoSource(
        string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        return ImageSource.FromStream(
            () =>
            {
                try
                {
                    byte[] bytes =
                        Convert.FromBase64String(base64);

                    return new MemoryStream(bytes);
                }
                catch
                {
                    return Stream.Null;
                }
            });
    }


    private static string Capitalizar(
        string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "";
        }

        CultureInfo es =
            CultureInfo.GetCultureInfo("es-ES");

        return char.ToUpper(texto[0], es) + texto[1..];
    }


    private static string FormatearMesCompleto(
        DateTime fecha)
    {
        return Capitalizar(
            fecha.ToString(
                "MMMM yyyy",
                CultureInfo.GetCultureInfo("es-ES")));
    }


    private void MostrarCarga(
        string texto)
    {
        LblCargando.Text = texto;
        OverlayCargando.IsVisible = true;
    }


    private void OcultarCarga()
    {
        OverlayCargando.IsVisible = false;
    }


    private async void Volver_Tapped(
        object sender,
        TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }
}


// ============================================================================
// VIEW MODELS DE PRESENTACIÓN
// ============================================================================

public sealed class PersonaVisitaUi
{
    public int PersonaId { get; set; }

    /// <summary>«A» (anciano) o «SM» (siervo ministerial).</summary>
    public string EtiquetaNombramiento { get; set; } = "";

    public bool MostrarNombramiento =>
        !string.IsNullOrWhiteSpace(EtiquetaNombramiento);

    /// <summary>«PR», «PEsp» o «Mis.».</summary>
    public string EtiquetaServicio { get; set; } = "";

    public bool MostrarServicio =>
        !string.IsNullOrWhiteSpace(EtiquetaServicio);

    public bool EsInactivo { get; set; }

    /// <summary>Fila separadora («PUBLICADORES INACTIVOS»).</summary>
    public bool EsSeparador { get; set; }

    public bool EsPersona =>
        !EsSeparador;

    public string TextoSeparador { get; set; } = "";

    public bool MostrarCuadrosHoras { get; set; } = true;

    public int Grupo { get; set; }
    public string NombreVisual { get; set; } = "";
    public string DatosMinisterio { get; set; } = "";
    public bool MostrarDatosMinisterio =>
        !string.IsNullOrWhiteSpace(DatosMinisterio);

    public ImageSource? FotoSource { get; set; }

    public bool TieneAlertaHoras { get; set; }

    public bool MostrarAlertaHoras =>
        TieneAlertaHoras
        && MostrarCuadrosHoras;
    public int RequisitoHoras { get; set; }
    public double HorasAcumuladas { get; set; }
    public double MetaHoras { get; set; }
    public double DeficitHoras { get; set; }

    public ActividadMesUi Actividad1 { get; set; } = ActividadMesUi.Vacia();
    public ActividadMesUi Actividad2 { get; set; } = ActividadMesUi.Vacia();
    public ActividadMesUi Actividad3 { get; set; } = ActividadMesUi.Vacia();
    public ActividadMesUi Actividad4 { get; set; } = ActividadMesUi.Vacia();
    public ActividadMesUi Actividad5 { get; set; } = ActividadMesUi.Vacia();
    public ActividadMesUi Actividad6 { get; set; } = ActividadMesUi.Vacia();
}


public sealed class ActividadMesUi
{
    public string Mes { get; set; } = "";
    public string Valor { get; set; } = "–";
    public string Indicador { get; set; } = "";
    public string Notas { get; set; } = "";
    public bool TieneNotas { get; set; }
    public double ValorFontSize { get; set; } = 14;

    /// <summary>Horas acumuladas del año de servicio hasta este mes.</summary>
    public string Acumulado { get; set; } = "";

    public bool MostrarAcumulado =>
        !string.IsNullOrWhiteSpace(Acumulado);

    public Color AcumuladoColor { get; set; } =
        Color.FromArgb("#9EA4AE");

    public Color Fondo { get; set; } = Color.FromArgb("#242830");
    public Color TextoColor { get; set; } = Color.FromArgb("#8F969F");

    public static ActividadMesUi Vacia() =>
        new();
}


public sealed class ProgramaVisitaUi
{
    public VistaProgramaVisitaMobile Original { get; set; } = new();
    public string Hora { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Subtitulo { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public string Notas { get; set; } = "";

    public bool MostrarUbicacion =>
        !string.IsNullOrWhiteSpace(Ubicacion);

    public bool MostrarResponsable =>
        !string.IsNullOrWhiteSpace(Original.Responsable);

    public string Responsable =>
        Original.Responsable;

    public string DiaTurno =>
        string.Join(
            " · ",
            new[]
            {
                Original.Dia,
                Original.Turno
            }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

    public string CompaneroVisual =>
        string.IsNullOrWhiteSpace(Original.Companero)
            ? ""
            : "con " + Original.Companero;

    public bool MostrarCompanero =>
        !string.IsNullOrWhiteSpace(Original.Companero);

    public bool MostrarMapa =>
        Original.Latitud != 0
        || Original.Longitud != 0
        || !string.IsNullOrWhiteSpace(Original.Ubicacion);

    public string UbicacionVisual =>
        MostrarUbicacion
            ? "📍 " + Ubicacion
            : "";

    public bool MostrarNotas =>
        !string.IsNullOrWhiteSpace(Notas);
}


public sealed class TerritorioUi
{
    public VistaTerritorioMobile Original { get; set; } = new();
    public string Nombre { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Ultima { get; set; } = "";
    public bool MostrarUltima =>
        !string.IsNullOrWhiteSpace(Ultima);
}


public sealed class RecomendacionS2Ui
{
    public RecomendacionS2Mobile Original { get; set; } = new();

    /// <summary>
    /// «Sí» = aprobar en JW Hub; «No» = cancelar en JW Hub al enviar.
    /// Por defecto «No», igual que en Windows.
    /// </summary>
    public string Decision { get; set; } = "No";

    public bool Aprobar =>
        string.Equals(
            Decision,
            "Sí",
            StringComparison.Ordinal);
}
