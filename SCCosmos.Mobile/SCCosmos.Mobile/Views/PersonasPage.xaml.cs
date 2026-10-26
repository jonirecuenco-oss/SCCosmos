using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

#if ANDROID
using SCCosmos.Mobile.Platforms.Android;
#endif

namespace SCCosmos.Mobile.Views;


public partial class PersonasPage
    : ContentPage, IActualizableMobile
{
    private List<Persona>
        _todasLasPersonas =
            new();


    private FiltrosPersonas
        _filtros =
            new();


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public PersonasPage()
    {
        InitializeComponent();
    }


    // =========================================================
    // ON APPEARING
    // =========================================================

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // La esposa del SC no ve los nombramientos ni las herramientas de
        // sincronización con JW Hub.
        if (!PerfilAccesoService.AccesoCompleto)
        {
            ToolbarItems.Clear();

            BtnSintonizarHub.IsVisible = false;
            BtnCancelarHub.IsVisible = false;
            BtnRevisarHub.IsVisible = false;
        }


        // Evitamos duplicar suscripciones.
        JwHubSyncState.Cambiado -=
            EstadoJwHub_Cambiado;


        JwHubSyncState.Cambiado +=
            EstadoJwHub_Cambiado;


        // =====================================================
        // FILTROS PROCEDENTES DE INICIO
        // =====================================================

        FiltrosPersonas? filtrosPendientes =
            PersonasNavigationState
                .Consumir();


        if (filtrosPendientes != null)
        {
            _filtros =
                filtrosPendientes;


            if (!string.IsNullOrEmpty(
                    TxtBuscar.Text))
            {
                TxtBuscar.Text =
                    "";
            }
        }


        // =====================================================
        // ESTADO JW HUB
        //
        // Aquí también decidimos si debe aparecer
        // "Revisar resultados JW Hub".
        // =====================================================

        ActualizarEstadoSintonizador();


        var reloj = System.Diagnostics.Stopwatch.StartNew();
        await CargarPersonasAsync();
        Services.Perf.Registrar("Personas", reloj.ElapsedMilliseconds);
    }


    // =========================================================
    // ON DISAPPEARING
    // =========================================================

    protected override void OnDisappearing()
    {
        JwHubSyncState.Cambiado -=
            EstadoJwHub_Cambiado;


        base.OnDisappearing();
    }


    // =========================================================
    // CARGAR PERSONAS
    // =========================================================

    public void DatosActualizados()
    {
        try
        {
            _ = CargarPersonasAsync();
        }
        catch
        {
        }
    }


    private async Task CargarPersonasAsync()
    {
        try
        {
            if (!DatabaseHelper.ExisteBaseLocal)
            {
                ListaPersonas.ItemsSource =
                    Array.Empty<Persona>();


                LblTotal.Text =
                    "Base de datos no disponible";


                return;
            }


            // Sin fotos: se piden solo para las fichas visibles.
            _todasLasPersonas =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerPersonas(incluirFoto: false)
                            .Where(
                                p =>
                                    string.IsNullOrWhiteSpace(
                                        p.FechaEliminacion))
                            .ToList());


            AplicarFiltros();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Personas",
                $"No se pudo cargar la lista:\n{ex.Message}",
                "Aceptar");
        }
    }


    // =========================================================
    // BUSCADOR
    // =========================================================

    private void TxtBuscar_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        AplicarFiltros();
    }


    // =========================================================
    // APLICAR FILTROS
    // =========================================================

    private void AplicarFiltros()
    {
        string busqueda =
            TxtBuscar.Text?.Trim()
            ?? "";


        IEnumerable<Persona> consulta =
            _todasLasPersonas;


        // =====================================================
        // BUSCADOR
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                busqueda))
        {
            consulta =
                consulta.Where(p =>
                    Contiene(
                        p.Nombre,
                        busqueda)
                    ||
                    Contiene(
                        p.NombreComun,
                        busqueda));
        }


        // =====================================================
        // NOMBRAMIENTO
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                _filtros.Nombramiento))
        {
            consulta =
                consulta.Where(p =>
                    string.Equals(
                        p.Nombramiento,
                        _filtros.Nombramiento,
                        StringComparison
                            .OrdinalIgnoreCase));
        }


        // =====================================================
        // SEXO
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                _filtros.Sexo))
        {
            consulta =
                consulta.Where(p =>
                    string.Equals(
                        p.Sexo,
                        _filtros.Sexo,
                        StringComparison
                            .OrdinalIgnoreCase));
        }


        // =====================================================
        // ESPERANZA
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                _filtros.Esperanza))
        {
            consulta =
                consulta.Where(p =>
                    string.Equals(
                        p.Esperanza,
                        _filtros.Esperanza,
                        StringComparison
                            .OrdinalIgnoreCase));
        }


        // =====================================================
        // CONGREGACIONES
        // =====================================================

        if (_filtros.Congregaciones.Count >
            0)
        {
            consulta =
                consulta.Where(p =>
                    _filtros
                        .Congregaciones
                        .Any(c =>
                            string.Equals(
                                c,
                                p.CongregacionNombre,
                                StringComparison
                                    .OrdinalIgnoreCase)));
        }


        // =====================================================
        // SERVICIO
        // =====================================================

        if (_filtros.Servicios.Count >
            0)
        {
            consulta =
                consulta.Where(p =>
                    _filtros
                        .Servicios
                        .Any(s =>
                            string.Equals(
                                s,
                                p.Servicio,
                                StringComparison
                                    .OrdinalIgnoreCase)));
        }


        // =====================================================
        // SOLICITUDES
        // =====================================================

        if (_filtros.Solicitudes.Count >
            0)
        {
            consulta =
                consulta.Where(p =>
                    _filtros
                        .Solicitudes
                        .Any(s =>
                            Contiene(
                                p.Solicitudes,
                                s)));
        }


        // =====================================================
        // PRIVILEGIOS
        // =====================================================

        if (_filtros.Privilegios.Count >
            0)
        {
            consulta =
                consulta.Where(p =>
                    _filtros
                        .Privilegios
                        .Any(pr =>
                            Contiene(
                                p.Privilegios,
                                pr)));
        }


        // =====================================================
        // RESULTADO
        // =====================================================

        List<Persona> resultado =
            consulta
                .OrderBy(p =>
                    p.Nombre)
                .ToList();


        ListaPersonas.ItemsSource =
            resultado
                .Select(p => new PersonaListaUi(p))
                .ToList();


        LblTotal.Text =
            resultado.Count ==
            _todasLasPersonas.Count
                ? $"{resultado.Count} personas"
                : $"{resultado.Count} de " +
                  $"{_todasLasPersonas.Count} personas";


        ActualizarIndicadorFiltro();
    }


    // =========================================================
    // CONTIENE
    // =========================================================

    private static bool Contiene(
        string? texto,
        string busqueda)
    {
        return
            !string.IsNullOrWhiteSpace(
                texto)
            &&
            texto.Contains(
                busqueda,
                StringComparison
                    .OrdinalIgnoreCase);
    }


    // =========================================================
    // INDICADOR DE FILTRO
    // =========================================================

    private void ActualizarIndicadorFiltro()
    {
        if (!_filtros.TieneFiltros)
        {
            PanelFiltroActivo.IsVisible =
                false;


            LblFiltroActivo.Text =
                "";


            return;
        }


        var textos =
            new List<string>();


        if (!string.IsNullOrWhiteSpace(
                _filtros.Nombramiento))
        {
            textos.Add(
                _filtros.Nombramiento);
        }


        if (!string.IsNullOrWhiteSpace(
                _filtros.Sexo))
        {
            textos.Add(
                _filtros.Sexo);
        }


        if (!string.IsNullOrWhiteSpace(
                _filtros.Esperanza))
        {
            textos.Add(
                _filtros.Esperanza);
        }


        if (_filtros.Congregaciones.Count >
            0)
        {
            textos.Add(
                _filtros.Congregaciones.Count ==
                1
                    ? _filtros
                        .Congregaciones[0]
                    : $"{_filtros.Congregaciones.Count} " +
                      "congregaciones");
        }


        if (_filtros.Servicios.Count >
            0)
        {
            textos.Add(
                _filtros.Servicios.Count ==
                1
                    ? _filtros
                        .Servicios[0]
                    : $"{_filtros.Servicios.Count} " +
                      "servicios");
        }


        if (_filtros.Solicitudes.Count >
            0)
        {
            textos.Add(
                string.Join(
                    ", ",
                    _filtros.Solicitudes));
        }


        if (_filtros.Privilegios.Count >
            0)
        {
            textos.Add(
                _filtros.Privilegios.Count ==
                1
                    ? _filtros
                        .Privilegios[0]
                    : $"{_filtros.Privilegios.Count} " +
                      "privilegios");
        }


        LblFiltroActivo.Text =
            string.Join(
                " · ",
                textos);


        PanelFiltroActivo.IsVisible =
            true;
    }


    // =========================================================
    // QUITAR FILTRO
    // =========================================================

    private void BtnQuitarFiltro_Click(
        object sender,
        EventArgs e)
    {
        _filtros =
            new FiltrosPersonas();


        AplicarFiltros();
    }


    // =========================================================
    // ABRIR FILTROS
    // =========================================================

    private async void BtnFiltros_Click(
        object sender,
        EventArgs e)
    {
        List<string> congregaciones =
            _todasLasPersonas
                .Select(p =>
                    p.CongregacionNombre)
                .Where(c =>
                    !string.IsNullOrWhiteSpace(
                        c)
                    &&
                    c !=
                    "Sin asignar")
                .Distinct(
                    StringComparer
                        .OrdinalIgnoreCase)
                .OrderBy(c =>
                    c)
                .ToList();


        var pagina =
            new FiltrosPersonasPage(
                congregaciones,
                _filtros,
                filtros =>
                {
                    _filtros =
                        filtros;


                    AplicarFiltros();
                });


        await Navigation.PushAsync(pagina);
    }


    // =========================================================
    // ABRIR PERSONA
    // =========================================================

    private async void Persona_Tapped(
        object sender,
        TappedEventArgs e)
    {
        int id =
            e.Parameter switch
            {
                PersonaListaUi fila => fila.Id,
                Persona persona => persona.Id,
                _ => 0
            };

        if (id <= 0)
        {
            return;
        }


        await Navigation.PushAsync(
            new PersonaDetallePage(id));
    }


    // =========================================================
    // NOMBRAMIENTOS
    // =========================================================

    private async void BtnNombramientos_Click(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new NombramientosPage());
    }


    // =========================================================
    // REFRESCAR
    // =========================================================

    /// <summary>
    /// Fila de la lista: expone lo que pinta la plantilla y carga la foto
    /// solo cuando la ficha llega a ser visible.
    /// </summary>
    public sealed class PersonaListaUi
    {
        private static readonly Dictionary<int, string> CacheFotos =
            new();

        private readonly Persona _persona;

        private string? _foto;

        public PersonaListaUi(Persona persona)
        {
            _persona = persona;
        }

        public int Id => _persona.Id;

        public string NombreMostrarTabla =>
            _persona.NombreMostrarTabla;

        public string CongregacionNombre =>
            _persona.CongregacionNombre;

        public string Nombramiento =>
            _persona.Nombramiento;

        public string Servicio =>
            _persona.Servicio;

        public string Solicitudes =>
            _persona.Solicitudes;

        public string Foto
        {
            get
            {
                if (_foto is null)
                {
                    if (!CacheFotos.TryGetValue(_persona.Id, out string? foto))
                    {
                        foto = DatabaseHelper.ObtenerFotoPersonaMobile(_persona.Id);
                        CacheFotos[_persona.Id] = foto;
                    }

                    _foto = foto;
                }

                return _foto;
            }
        }
    }


    private async void RefrescarLista_Refreshing(
        object sender,
        EventArgs e)
    {
        try
        {
            await CargarPersonasAsync();
        }
        finally
        {
            RefrescarLista.IsRefreshing =
                false;
        }
    }


    // =========================================================
    // INICIAR SINCRONIZACIÓN
    // =========================================================

    private void BtnSintonizarHub_Click(
        object sender,
        EventArgs e)
    {
        if (JwHubSyncController
            .EstaSintonizando)
        {
            return;
        }


        // Si hubiera un resultado anterior,
        // lo ocultamos visualmente mientras empieza
        // una nueva sincronización.
        BtnRevisarHub.IsVisible =
            false;


        JwHubSyncController
            .IniciarManual();


        ActualizarEstadoSintonizador();
    }


    // =========================================================
    // CANCELAR SINCRONIZACIÓN
    // =========================================================

    private void BtnCancelarHub_Click(
        object sender,
        EventArgs e)
    {
        JwHubSyncController
            .Cancelar();
    }


    // =========================================================
    // EVENTO DE CAMBIO JW HUB
    // =========================================================

    private void EstadoJwHub_Cambiado()
    {
        MainThread
            .BeginInvokeOnMainThread(
                ActualizarEstadoSintonizador);
    }


    // =========================================================
    // ACTUALIZAR ESTADO DEL SINCRONIZADOR
    // =========================================================

    private void ActualizarEstadoSintonizador()
    {
        JwHubSyncSnapshot estado =
            JwHubSyncState.Actual;


        // =====================================================
        // MENSAJE
        // =====================================================

        LblEstadoHub.IsVisible =
            !string.IsNullOrWhiteSpace(
                estado.Mensaje);


        LblEstadoHub.Text =
            estado.Mensaje;


        LblEstadoHub.TextColor =
            estado.Estado switch
            {
                EstadoJwHubSync.Completado =>
                    Colors.LightGreen,

                EstadoJwHubSync.Error =>
                    Colors.OrangeRed,

                EstadoJwHubSync
                    .RequiereInicioSesion =>
                    Colors.Orange,

                EstadoJwHubSync.Cancelado =>
                    Colors.Orange,

                _ =>
                    Microsoft.Maui.Graphics.Color.FromArgb(
                        "#9EA4AE")
            };


        // =====================================================
        // BOTONES PRINCIPALES
        // =====================================================

        BtnSintonizarHub.IsEnabled =
            !estado.EnCurso;


        BtnCancelarHub.IsVisible =
            estado.EnCurso;


        // =====================================================
        // BOTÓN REVISAR RESULTADOS
        //
        // Debe aparecer siempre que exista un análisis
        // pendiente y no haya una sincronización en curso.
        //
        // Esto cubre tanto:
        //
        // - extracción completa (por ejemplo 769/769)
        // - extracción parcial con incidencias
        //   (por ejemplo 768/769)
        // - recuperación de una revisión persistida después
        //   de que Android haya cerrado el proceso.
        //
        // En una extracción parcial, el servicio genera el
        // análisis con calcularBajas:false, por lo que nunca
        // se presentan bajas inseguras.
        // =====================================================

#if ANDROID

        bool hayAnalisis =
            JwHubSyncAnalysisService
                .UltimoAnalisis
                is not null;


        // Si la revisión ya se finalizó, no conservamos en
        // pantalla el resultado de la extracción anterior.
        //
        // Es una defensa adicional al reseteo que hace
        // JwHubSyncReviewPage al pulsar "Finalizar revisión".
        if (!hayAnalisis
            &&
            estado.Estado ==
                EstadoJwHubSync.Completado)
        {
            BtnRevisarHub.IsVisible =
                false;

            BtnSintonizarHub.IsEnabled =
                true;

            BtnSintonizarHub.Text =
                "Sincronizar JW Hub";

            BtnCancelarHub.IsVisible =
                false;

            ProgresoHub.IsVisible =
                false;

            ProgresoHub.Progress =
                0;

            LblEstadoHub.Text =
                "";

            LblEstadoHub.IsVisible =
                false;

            return;
        }


        BtnRevisarHub.IsVisible =
            hayAnalisis
            &&
            !estado.EnCurso;

#else

        BtnRevisarHub.IsVisible =
            false;

#endif


        // =====================================================
        // PROGRESO
        // =====================================================

        ProgresoHub.IsVisible =
            estado.EnCurso;


        if (estado.Total > 0)
        {
            ProgresoHub.Progress =
                Math.Clamp(
                    (double)estado.Actual /
                    estado.Total,
                    0,
                    1);
        }
        else
        {
            ProgresoHub.Progress =
                0;
        }


        // =====================================================
        // SINCRONIZACIÓN EN CURSO
        // =====================================================

        if (estado.EnCurso)
        {
            BtnSintonizarHub.Text =
                estado.Total > 0
                    ? $"Sincronizando " +
                      $"{estado.Actual}/" +
                      $"{estado.Total}"
                    : string.IsNullOrWhiteSpace(
                        estado.Mensaje)
                        ? "Preparando JW Hub..."
                        : estado.Mensaje;


            return;
        }


        // =====================================================
        // SINCRONIZACIÓN TERMINADA
        // =====================================================

        BtnSintonizarHub.Text =
            estado.Estado switch
            {
                EstadoJwHubSync.Completado =>
                    "✓ Sincronización completada",

                EstadoJwHubSync.Cancelado =>
                    "Sincronizar JW Hub",

                EstadoJwHubSync.Error =>
                    "⚠ Sincronizar JW Hub",

                EstadoJwHubSync
                    .RequiereInicioSesion =>
                    "Iniciar sesión en JW Hub",

                _ =>
                    "Sincronizar JW Hub"
            };
    }


    // =========================================================
    // REVISAR RESULTADOS JW HUB
    // =========================================================

    private async void BtnRevisarHub_Clicked(
        object sender,
        EventArgs e)
    {
#if ANDROID

        // Protección adicional.
        //
        // El botón normalmente solo aparece cuando
        // existe este resultado, pero lo comprobamos
        // igualmente antes de navegar.

        if (JwHubSyncAnalysisService
                .UltimoAnalisis
            is null)
        {
            await DisplayAlert(
                "JW Hub",
                "No hay resultados de sincronización " +
                "disponibles para revisar.",
                "Aceptar");


            BtnRevisarHub.IsVisible =
                false;


            return;
        }

#endif


        await Navigation.PushAsync(
            new JwHubSyncReviewPage());
    }
}