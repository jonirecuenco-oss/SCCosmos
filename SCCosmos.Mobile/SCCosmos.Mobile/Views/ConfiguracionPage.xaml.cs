using SCCosmos.Mobile.Services;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using SCCosmos.Mobile.Data;
using System.ComponentModel;

#if ANDROID
using SCCosmos.Mobile.Platforms.Android;
#endif

namespace SCCosmos.Mobile.Views;

public partial class ConfiguracionPage : ContentPage
{
    private bool _cargandoConfiguracionJwHub;

    public ConfiguracionPage()
    {
        InitializeComponent();
        HookAutoguardado();
        CargarConfiguracionJwHub();
        CargarDatosGlobales();
        CargarPerfil();

        // Claves opcionales para los tiempos de viaje (TomTom/Google).
        TxtTomTomKey.Text =
            TiempoViajeService.ObtenerClaveTomTom();

        TxtGmapsKey.Text =
            TiempoViajeService.ObtenerClaveGoogle();

        ActualizarMensajeClavesViaje();

        // La sincronización es siempre automática: no hay interruptor.
        AutoSyncService.Activo =
            true;

        // Selector nuevo entre dispositivos.
        CargarSincronizacion();

        // Versión y actualizaciones.
        CargarActualizaciones();

        // La sincronización entre dispositivos también la ajusta la
        // esposa: su móvil necesita sus propias direcciones y clave.
    }


    // =========================================================
    // VERSIÓN Y ACTUALIZACIONES
    // =========================================================

    private bool _cargandoActualizaciones;

    private void CargarActualizaciones()
    {
        _cargandoActualizaciones =
            true;

        LblVersionApp.Text =
            "Versión instalada: "
            + ActualizacionMobileService.VersionInstalada;

        SwitchActualizarAuto.IsToggled =
            ActualizacionMobileService.AutoActiva;

        _cargandoActualizaciones =
            false;
    }


    private void SwitchActualizarAuto_Toggled(
        object sender,
        ToggledEventArgs e)
    {
        if (_cargandoActualizaciones)
        {
            return;
        }

        ActualizacionMobileService.AutoActiva =
            e.Value;

        LblEstadoActualizacion.Text =
            e.Value
                ? "Se actualizará sola cuando haya una versión nueva."
                : "Se te avisará cuando haya una versión nueva.";
    }


    private async void BtnBuscarActualizacion_Clicked(
        object sender,
        EventArgs e)
    {
        BtnBuscarActualizacion.IsEnabled =
            false;

        LblEstadoActualizacion.Text =
            "Buscando…";

        try
        {
            var resultado =
                await ActualizacionMobileService
                    .ComprobarAsync();

            if (!resultado.Correcto)
            {
                LblEstadoActualizacion.Text =
                    "No se pudo comprobar: "
                    + resultado.Error;

                return;
            }

            if (resultado.Version == null)
            {
                LblEstadoActualizacion.Text =
                    "Todavía no hay ninguna versión publicada.";

                return;
            }

            if (!ActualizacionMobileService.HayNueva(
                    resultado))
            {
                LblEstadoActualizacion.Text =
                    "Estás al día (v"
                    + ActualizacionMobileService.VersionInstalada
                    + ").";

                return;
            }

            var version =
                resultado.Version;

            bool actualizar =
                await DisplayAlert(
                    "Actualización disponible",
                    "Hay una versión nueva de SCCosmos (v"
                    + version.Numero + ").\n\n"
                    + RecortarNotas(version.Notas)
                    + "¿Descargarla e instalarla ahora?",
                    "Actualizar",
                    "Más tarde");

            if (!actualizar)
            {
                LblEstadoActualizacion.Text =
                    "Actualización aplazada (v"
                    + version.Numero + ").";

                return;
            }

            await DescargarEInstalarAsync(
                version);
        }
        catch (Exception ex)
        {
            LblEstadoActualizacion.Text =
                "No se pudo comprobar: " + ex.Message;
        }
        finally
        {
            BtnBuscarActualizacion.IsEnabled =
                true;
        }
    }


    /// <summary>
    /// Descarga el APK y abre el instalador del sistema.
    /// </summary>
    private async Task DescargarEInstalarAsync(
        SCCosmos.Sincro.VersionPublicada version)
    {
        LblEstadoActualizacion.Text =
            "Descargando la versión nueva…";

        bool descargada =
            await ActualizacionMobileService
                .DescargarAsync(
                    version,
                    progreso =>
                        MainThread.BeginInvokeOnMainThread(
                            () =>
                                LblEstadoActualizacion.Text =
                                    "Descargando la versión nueva… "
                                    + progreso.ToString("P0")));

        if (!descargada)
        {
            LblEstadoActualizacion.Text =
                "No se pudo descargar la actualización.";

            return;
        }

        LblEstadoActualizacion.Text =
            "Abriendo el instalador…";

        if (ActualizacionMobileService.Instalar())
        {
            LblEstadoActualizacion.Text =
                "Sigue las indicaciones de Android para instalar la "
                + "versión v" + version.Numero + ".";
        }
        else
        {
            LblEstadoActualizacion.Text =
                "No se pudo abrir el instalador. Revisa que SCCosmos "
                + "tenga permiso para instalar aplicaciones.";
        }
    }


    private static string RecortarNotas(
        string notas)
    {
        string texto =
            (notas ?? "").Trim();

        if (texto.Length > 400)
        {
            texto =
                texto.Substring(0, 400) + "…";
        }

        return texto.Length == 0
            ? ""
            : texto + "\n\n";
    }

    // =========================================================
    // SINCRONIZACIÓN ENTRE DISPOSITIVOS
    // =========================================================

    private Microsoft.Maui.Dispatching.IDispatcherTimer?
        _timerSincro;


    private void CargarSincronizacion()
    {
        try
        {
            _autoguardando =
                true;

            SwSincroActivo.IsToggled =
                SincronizacionMobile.ConfiguracionActiva;

            TxtNombreSincro.Text =
                SincronizacionMobile.NombreDispositivo;

            TxtClaveSincro.Text =
                SincronizacionMobile.Clave;

            TxtDireccionesSincro.Text =
                SincronizacionMobile.DireccionesTexto;

            _autoguardando =
                false;

            RefrescarSincro();
        }
        catch
        {
            _autoguardando =
                false;
        }
    }


    private void RefrescarSincro()
    {
        try
        {
            LblEstadoSincro.Text =
                SincronizacionMobile.EstadoTexto;

            int personas =
                DatabaseHelper.ContarPersonasMobile();

            string ultima =
                SincronizacionMobile.UltimaRecepcion;

            LblDatosLocales.Text =
                ultima.Length == 0
                    ? $"Personas en este móvil: {personas} · Aún no se han recibido cambios de otro dispositivo."
                    : $"Personas en este móvil: {personas} · Última recepción de cambios: {ultima}.";

            BindableLayout.SetItemsSource(
                ListaDispositivosSincro,
                SincronizacionMobile.ObtenerDispositivosVista());
        }
        catch
        {
        }
    }


    private async void BtnRepararSincro_Click(
        object? sender,
        EventArgs e)
    {
        bool continuar =
            await DisplayAlert(
                "Re-sincronizar todo",
                "Se pedirá a los demás dispositivos su estado completo y se enviará el de este móvil. " +
                "Úsalo si aquí ves datos más antiguos que en los otros equipos. ¿Continuar?",
                "Sí, reparar",
                "Cancelar");

        if (!continuar)
        {
            return;
        }

        try
        {
            SincronizacionMobile.ReiniciarIntercambio();

            RefrescarSincro();

            await DisplayAlert(
                "Re-sincronización",
                "Hecho. Deja la app abierta y cerca de los otros dispositivos unos segundos: " +
                "los datos que faltaban irán llegando solos.",
                "Vale");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Re-sincronización",
                "No se pudo reiniciar: " + ex.Message,
                "Vale");
        }
    }


    private void IniciarTimerSincro()
    {
        try
        {
            _timerSincro ??=
                Dispatcher.CreateTimer();

            _timerSincro.Interval =
                TimeSpan.FromSeconds(3);

            _timerSincro.Tick -=
                TimerSincro_Tick;

            _timerSincro.Tick +=
                TimerSincro_Tick;

            _timerSincro.Start();

            RefrescarSincro();
        }
        catch
        {
        }
    }


    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        try
        {
            _timerSincro?.Stop();
        }
        catch
        {
        }
    }


    private void TimerSincro_Tick(
        object? sender,
        EventArgs e)
    {
        RefrescarSincro();
    }


    /// <summary>
    /// Aplica los ajustes de sincronización (sin botón: se llama solo al
    /// cambiar el interruptor o al salir de los campos).
    /// </summary>
    private void AplicarSincroAutomatico()
    {
        try
        {
            if (_autoguardando)
            {
                return;
            }

            bool activoAntes =
                SincronizacionMobile.ConfiguracionActiva;

            SincronizacionMobile.ConfiguracionActiva =
                SwSincroActivo.IsToggled;

            string nombreNuevo =
                (TxtNombreSincro.Text ?? "").Trim();

            string claveNueva =
                (TxtClaveSincro.Text ?? "").Trim();

            string direccionesNuevas =
                (TxtDireccionesSincro.Text ?? "").Trim();

            bool cambio =
                activoAntes != SincronizacionMobile.ConfiguracionActiva
                || !string.Equals(
                    nombreNuevo,
                    SincronizacionMobile.NombreDispositivo,
                    StringComparison.Ordinal)
                || !string.Equals(
                    claveNueva,
                    SincronizacionMobile.Clave,
                    StringComparison.Ordinal)
                || !string.Equals(
                    direccionesNuevas,
                    SincronizacionMobile.DireccionesTexto.Trim(),
                    StringComparison.Ordinal);

            if (!cambio)
            {
                return;
            }

            SincronizacionMobile.NombreDispositivo =
                nombreNuevo;

            SincronizacionMobile.Clave =
                claveNueva;

            SincronizacionMobile.DireccionesTexto =
                direccionesNuevas;

            SincronizacionMobile.Aplicar();

            if (activoAntes != SincronizacionMobile.ConfiguracionActiva)
            {
                DatabaseHelper.AnotarActividadMobile(
                    "Configuración",
                    SincronizacionMobile.ConfiguracionActiva
                        ? "Activó la sincronización entre dispositivos"
                        : "Desactivó la sincronización entre dispositivos");
            }
            else
            {
                DatabaseHelper.AnotarActividadMobile(
                    "Configuración",
                    "Cambió los datos de sincronización (nombre o clave)");
            }

            RefrescarSincro();
        }
        catch
        {
        }
    }


    /// <summary>
    /// Desmarca o marca un dispositivo para no sincronizar con él.
    /// </summary>
    private void ChkDispositivo_Changed(
        object? sender,
        CheckedChangedEventArgs e)
    {
        try
        {
            if (_autoguardando)
            {
                return;
            }

            if (sender is CheckBox casilla
                && casilla.BindingContext is DispositivoSyncMobile dispositivo
                && dispositivo.Id.Length > 0)
            {
                SincronizacionMobile.EstablecerHabilitado(
                    dispositivo.Id,
                    dispositivo.Nombre,
                    e.Value);
            }
        }
        catch
        {
        }
    }

    private void CargarPerfil()
    {
        try
        {
            PickerPerfil.SelectedItem =
                PerfilAccesoService.EsHombre
                    ? PerfilAccesoService.PefilHombre
                    : (PerfilAccesoService.Configurado
                        ? PerfilAccesoService.PerfilEsposa
                        : PerfilAccesoService.PefilHombre);

            TxtCodigoPerfil.Text =
                PerfilAccesoService.CodigoValidado
                    ? PerfilAccesoService.MarcadorCodigo
                    : "";

            ActualizarPanelCodigoPerfil();
            ActualizarEstadoPerfil();
        }
        catch
        {
        }
    }

    /// <summary>
    /// El código de acceso es solo para el superintendente: el campo se
    /// oculta mientras el perfil seleccionado no sea «Hombre».
    /// </summary>
    private void ActualizarPanelCodigoPerfil()
    {
        bool esHombre =
            string.Equals(
                PickerPerfil.SelectedItem?.ToString(),
                PerfilAccesoService.PefilHombre,
                StringComparison.OrdinalIgnoreCase);

        PanelCodigoPerfil.IsVisible =
            esHombre;

        if (!esHombre)
        {
            TxtCodigoPerfil.Text = "";
        }
    }

    private void PickerPerfil_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        ActualizarPanelCodigoPerfil();
    }

    private void TxtTomTomKey_Unfocused(
        object? sender,
        FocusEventArgs e)
    {
        try
        {
            TiempoViajeService.GuardarClaveTomTom(
                TxtTomTomKey.Text);

            ActualizarMensajeClavesViaje();
        }
        catch
        {
        }
    }


    private void TxtGmapsKey_Unfocused(
        object? sender,
        FocusEventArgs e)
    {
        try
        {
            TiempoViajeService.GuardarClaveGoogle(
                TxtGmapsKey.Text);

            ActualizarMensajeClavesViaje();
        }
        catch
        {
        }
    }


    private void ActualizarMensajeClavesViaje()
    {
        if (!string.IsNullOrWhiteSpace(
                TiempoViajeService.ObtenerClaveTomTom()))
        {
            LblGmapsGuardada.Text =
                "Clave de TomTom guardada: los tiempos " +
                "incluirán el tráfico.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                TiempoViajeService.ObtenerClaveGoogle()))
        {
            LblGmapsGuardada.Text =
                "Clave de Google guardada: los tiempos " +
                "incluirán el tráfico.";
            return;
        }

        LblGmapsGuardada.Text =
            "Sin clave: el tiempo de viaje no se mostrará.";
    }


    private void ActualizarEstadoPerfil()
    {
        bool completo =
            PerfilAccesoService.AccesoCompleto;

        LblPerfilEstado.TextColor =
            completo
                ? Colors.LightGreen
                : Colors.Orange;

        LblPerfilEstado.Text =
            completo
                ? "Acceso completo a todas las funciones."
                : "Acceso limitado: Inicio, Calendario, Personas y " +
                  "Configuración.";

        AplicarPerfilAccesoConfiguracion();
    }


    /// <summary>
    /// La esposa del SC no ve el JW Hub ni los datos del superintendente y
    /// del circuito: solo su propio nombre.
    /// </summary>
    private void AplicarPerfilAccesoConfiguracion()
    {
        bool completo =
            PerfilAccesoService.AccesoCompleto;

        TituloJwHub.IsVisible = completo;
        PanelJwHub.IsVisible = completo;

        PanelDatosSC.IsVisible = completo;
        PanelDatosSCFinal.IsVisible = completo;
    }

    private async void BtnGuardarPerfil_Click(
        object sender,
        EventArgs e)
    {
        try
        {
            string rol =
                PickerPerfil.SelectedItem?.ToString()
                ?? PerfilAccesoService.PefilHombre;

            PerfilAccesoService.EstablecerRol(
                rol);

            if (PerfilAccesoService.EsHombre)
            {
                string escrito =
                    (TxtCodigoPerfil.Text ?? "")
                        .Trim();

                // El marcador significa "ya validado": no se vuelve a
                // comprobar (el código real ya no se muestra).
                if (!string.Equals(
                        escrito,
                        PerfilAccesoService.MarcadorCodigo,
                        StringComparison.Ordinal))
                {
                    PerfilAccesoService.ValidarCodigo(
                        escrito);
                }
            }

            ActualizarEstadoPerfil();

            (Shell.Current as AppShell)
                ?.AplicarPerfilAcceso();

            await DisplayAlert(
                "Perfil de usuario",
                PerfilAccesoService.AccesoCompleto
                    ? "Guardado. Acceso completo."
                    : "Guardado. Acceso limitado a Inicio, Calendario, " +
                      "Personas y Configuración.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Perfil",
                "No se pudo guardar: " + ex.Message,
                "Aceptar");
        }
    }

    private void CargarDatosGlobales()
    {
        try
        {
            _autoguardando =
                true;

            TxtNombreSC.Text =
                DatabaseHelper.ObtenerConfiguracion("NombreSC", "");

            TxtApellidoSC.Text =
                DatabaseHelper.ObtenerConfiguracion("ApellidoSC", "");

            TxtNombreEsposaSC.Text =
                DatabaseHelper.ObtenerConfiguracion("NombreEsposaSC", "");

            TxtApellidoEsposaSC.Text =
                DatabaseHelper.ObtenerConfiguracion("ApellidoEsposaSC", "");

            TxtNumCircuito.Text =
                DatabaseHelper.ObtenerConfiguracion("NumeroCircuito", "");

            TxtRutaArchivoCircuito.Text =
                DatabaseHelper.ObtenerConfiguracion("RutaArchivoCircuito", "");

            _autoguardando =
                false;
        }
        catch (Exception ex)
        {
            _autoguardando =
                false;

            LblResultadoGlobal.TextColor =
                Colors.OrangeRed;

            LblResultadoGlobal.Text =
                "No se pudieron leer los datos: " + ex.Message;
        }
    }

    /// <summary>
    /// Guarda los datos del circuito (sin botones: se llama solo al
    /// salir de cada campo).
    /// </summary>
    private void GuardarDatosGlobales()
    {
        try
        {
            if (_autoguardando)
            {
                return;
            }

            DatabaseHelper.GuardarConfiguracion(
                "NombreSC",
                TxtNombreSC.Text?.Trim() ?? "");

            DatabaseHelper.GuardarConfiguracion(
                "ApellidoSC",
                TxtApellidoSC.Text?.Trim() ?? "");

            DatabaseHelper.GuardarConfiguracion(
                "NombreEsposaSC",
                TxtNombreEsposaSC.Text?.Trim() ?? "");

            DatabaseHelper.GuardarConfiguracion(
                "ApellidoEsposaSC",
                TxtApellidoEsposaSC.Text?.Trim() ?? "");

            DatabaseHelper.GuardarConfiguracion(
                "NumeroCircuito",
                TxtNumCircuito.Text?.Trim() ?? "");

            DatabaseHelper.GuardarConfiguracion(
                "RutaArchivoCircuito",
                TxtRutaArchivoCircuito.Text?.Trim() ?? "");

            LblResultadoGlobal.TextColor =
                Colors.LightGreen;

            LblResultadoGlobal.Text =
                "✓ Guardado automáticamente.";
        }
        catch (Exception ex)
        {
            LblResultadoGlobal.TextColor =
                Colors.OrangeRed;

            LblResultadoGlobal.Text =
                "✗ No se pudieron guardar: " + ex.Message;
        }
    }


    // =========================================================
    // AUTOGUARDADO (SIN BOTONES)
    // =========================================================

    private bool _autoguardando;

    private void HookAutoguardado()
    {
        foreach (Entry campo in
                 new Entry[]
                 {
                     TxtNombreSC,
                     TxtApellidoSC,
                     TxtNombreEsposaSC,
                     TxtApellidoEsposaSC,
                     TxtNumCircuito,
                     TxtRutaArchivoCircuito
                 })
        {
            campo.Unfocused +=
                (s, e) => GuardarDatosGlobales();
        }

        TxtNombreSincro.Unfocused +=
            (s, e) => AplicarSincroAutomatico();

        TxtClaveSincro.Unfocused +=
            (s, e) => AplicarSincroAutomatico();

        TxtDireccionesSincro.Unfocused +=
            (s, e) => AplicarSincroAutomatico();

        SwSincroActivo.Toggled +=
            (s, e) => AplicarSincroAutomatico();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        IniciarTimerSincro();

        // Si la sincronización automática ya estaba activada
        // (por ejemplo, tras actualizar/reinstalar una versión
        // sobre la anterior), comprobamos también el permiso
        // de notificaciones antes de volver a programarla.
        if (JwHubAutoSettings.Activo)
        {
            bool permisoNotificaciones =
                await AsegurarPermisoNotificacionesAsync(
                    mostrarAvisoSiDenegado: false);

            if (!permisoNotificaciones)
            {
                JwHubAutoSettings.Activo =
                    false;

                JwHubAutoScheduler
                    .Cancelar();

                _cargandoConfiguracionJwHub =
                    true;

                SwJwHubAutomatico.IsToggled =
                    false;

                _cargandoConfiguracionJwHub =
                    false;
            }
            else if (JwHubAutoScheduler
                         .PuedeProgramarExactamente)
            {
                JwHubAutoScheduler
                    .ProgramarSiguiente();
            }
        }

        ActualizarTextoJwHubAuto();
        ActualizarEstadoSesionJwHub();
    }

    private void CargarConfiguracionJwHub()
    {
        _cargandoConfiguracionJwHub =
            true;

        SwJwHubAutomatico.IsToggled =
            JwHubAutoSettings.Activo;

        HoraJwHub.Time =
            JwHubAutoSettings.Hora;

        _cargandoConfiguracionJwHub =
            false;

        ActualizarTextoJwHubAuto();
    }

    private async void SwJwHubAutomatico_Toggled(
        object sender,
        ToggledEventArgs e)
    {
        if (_cargandoConfiguracionJwHub)
            return;

        // =====================================================
        // DESACTIVAR
        // =====================================================

        if (!e.Value)
        {
            JwHubAutoSettings.Activo =
                false;

            JwHubAutoScheduler
                .Cancelar();

            ActualizarTextoJwHubAuto();

            return;
        }

        // =====================================================
        // ACTIVAR
        //
        // Primero pedimos permiso para notificaciones.
        //
        // Android 13+ no muestra notificaciones de una app
        // recién instalada hasta que el usuario concede
        // POST_NOTIFICATIONS.
        // =====================================================

        bool permisoNotificaciones =
            await AsegurarPermisoNotificacionesAsync(
                mostrarAvisoSiDenegado: true);

        if (!permisoNotificaciones)
        {
            JwHubAutoSettings.Activo =
                false;

            JwHubAutoScheduler
                .Cancelar();

            _cargandoConfiguracionJwHub =
                true;

            SwJwHubAutomatico.IsToggled =
                false;

            _cargandoConfiguracionJwHub =
                false;

            ActualizarTextoJwHubAuto();

            return;
        }

        JwHubAutoSettings.Activo =
            true;

        JwHubAutoSettings.Hora =
            HoraJwHub.Time
            ?? new TimeSpan(
                3,
                0,
                0);

        // =====================================================
        // PERMISO DE ALARMAS EXACTAS
        // =====================================================

        if (!JwHubAutoScheduler
                .PuedeProgramarExactamente)
        {
            await DisplayAlert(
                "Permiso necesario",
                "Android necesita permiso para que SCCosmos " +
                "pueda iniciar la sincronización automáticamente " +
                "a la hora elegida. En la siguiente pantalla " +
                "permite Alarmas y recordatorios para SCCosmos.",
                "Continuar");

            JwHubAutoScheduler
                .SolicitarPermiso();

            ActualizarTextoJwHubAuto();

            return;
        }

        // =====================================================
        // PROGRAMAR
        // =====================================================

        JwHubAutoScheduler
            .ProgramarSiguiente();

        ActualizarTextoJwHubAuto();
    }

    private void HoraJwHub_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_cargandoConfiguracionJwHub)
            return;

        if (e.PropertyName !=
            nameof(TimePicker.Time))
        {
            return;
        }

        JwHubAutoSettings.Hora =
            HoraJwHub.Time
            ?? new TimeSpan(
                3,
                0,
                0);

        if (JwHubAutoSettings.Activo &&
            JwHubAutoScheduler
                .PuedeProgramarExactamente)
        {
            JwHubAutoScheduler
                .ProgramarSiguiente();
        }

        ActualizarTextoJwHubAuto();
    }

    private void ActualizarTextoJwHubAuto()
    {
        if (!JwHubAutoSettings.Activo)
        {
            LblEstadoJwHubAuto.Text =
                "Desactivada";

            return;
        }

        LblEstadoJwHubAuto.Text =
            $"Activa · Todos los días a las " +
            $"{JwHubAutoSettings.Hora:hh\\:mm}";
    }

    private async void BtnSesionJwHub_Click(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new JwHubLoginPage());
    }

    private void ActualizarEstadoSesionJwHub()
    {
        if (!JwHubSessionState.SesionConfigurada)
        {
            LblSesionJwHub.Text =
                "No hay una sesión válida comprobada.";

            LblSesionJwHub.TextColor =
                Colors.Orange;

            return;
        }

        LblSesionJwHub.Text =
            string.IsNullOrWhiteSpace(
                JwHubSessionState.UltimaValidacion)

                ? "Sesión configurada."

                : $"Sesión comprobada: " +
                  $"{JwHubSessionState.UltimaValidacion}";

        LblSesionJwHub.TextColor =
            Colors.LightGreen;
    }

    // =========================================================
    // PERMISO DE NOTIFICACIONES
    //
    // Android 13 (API 33) y posteriores requieren permiso
    // explícito del usuario para mostrar notificaciones.
    //
    // En Android anteriores este permiso se considera concedido.
    // =========================================================

    private async Task<bool>
        AsegurarPermisoNotificacionesAsync(
            bool mostrarAvisoSiDenegado)
    {
#if ANDROID

        try
        {
            PermissionStatus estado =
                await Permissions
                    .CheckStatusAsync<
                        Permissions.PostNotifications>();

            if (estado ==
                PermissionStatus.Granted)
            {
                return true;
            }

            estado =
                await Permissions
                    .RequestAsync<
                        Permissions.PostNotifications>();

            if (estado ==
                PermissionStatus.Granted)
            {
                return true;
            }

            if (mostrarAvisoSiDenegado)
            {
                await DisplayAlert(
                    "Notificaciones desactivadas",
                    "SCCosmos necesita permiso para mostrarte " +
                    "un aviso cuando la sincronización automática " +
                    "detecte conflictos, altas, bajas o una " +
                    "incidencia.\n\n" +
                    "La sincronización automática permanecerá " +
                    "desactivada hasta que permitas las " +
                    "notificaciones para SCCosmos.",
                    "Aceptar");
            }

            return false;
        }
        catch
        {
            if (mostrarAvisoSiDenegado)
            {
                await DisplayAlert(
                    "Permiso de notificaciones",
                    "No se pudo comprobar el permiso de " +
                    "notificaciones. La sincronización automática " +
                    "no se activará para evitar que SCCosmos " +
                    "termine una revisión sin poder avisarte.",
                    "Aceptar");
            }

            return false;
        }

#else

        return true;

#endif
    }
}