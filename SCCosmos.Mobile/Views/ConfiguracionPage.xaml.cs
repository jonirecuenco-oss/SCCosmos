using SCCosmos.Mobile.Services;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using SCCosmos.Mobile.Data;
using System.ComponentModel;

namespace SCCosmos.Mobile.Views;

public partial class ConfiguracionPage : ContentPage
{
    private const string ClaveRutaDb = "RutaCarpetaSCCosmos";
    private bool _cargandoConfiguracionJwHub;

    public ConfiguracionPage()
    {
        InitializeComponent();
        CargarConfiguracionJwHub();
        CargarRutaGuardada();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

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

    private void CargarRutaGuardada()
    {
        if (SyncFolderService.EstaConfigurada)
        {
            LblRuta.Text =
                $"Carpeta: {SyncFolderService.NombreCarpeta}";

            ComprobarBaseDatos();
        }
        else
        {
            LblRuta.Text =
                "No se ha seleccionado ninguna carpeta.";
        }
    }

    private async void BtnSeleccionarCarpeta_Click(
        object sender,
        EventArgs e)
    {
        try
        {
            string? uri =
                await SyncFolderService
                    .SeleccionarCarpetaAsync();

            if (string.IsNullOrWhiteSpace(uri))
            {
                LblEstado.Text =
                    "No se seleccionó ninguna carpeta.";

                LblEstado.TextColor =
                    Colors.Orange;

                return;
            }

            LblRuta.Text =
                $"Carpeta: {SyncFolderService.NombreCarpeta}";

            ComprobarBaseDatos();
        }
        catch (Exception ex)
        {
            LblEstado.Text =
                $"Error al seleccionar la carpeta:\n{ex.Message}";

            LblEstado.TextColor =
                Colors.Red;
        }
    }

    private void BtnComprobar_Click(
        object sender,
        EventArgs e)
    {
        ComprobarBaseDatos();
    }

    private void ComprobarBaseDatos()
    {
        if (!SyncFolderService.EstaConfigurada)
        {
            LblEstado.Text =
                "Primero selecciona la carpeta SCCosmos.";

            LblEstado.TextColor =
                Colors.Orange;

            return;
        }

        if (SyncFolderService.ExisteBaseDatos())
        {
            LblEstado.Text =
                "✓ sccosmos.db encontrado y accesible";

            LblEstado.TextColor =
                Colors.LightGreen;
        }
        else
        {
            LblEstado.Text =
                "✗ No se encuentra sccosmos.db dentro de la carpeta seleccionada.";

            LblEstado.TextColor =
                Colors.OrangeRed;
        }
    }

    private async void BtnImportar_Click(
        object sender,
        EventArgs e)
    {
        LblResultadoDb.Text =
            "Actualizando base de datos...";

        var resultado =
            await DatabaseSyncService.ImportarAsync();

        if (resultado.Correcto)
        {
            LblResultadoDb.TextColor =
                Colors.LightGreen;

            LblResultadoDb.Text =
                "✓ " + resultado.Mensaje;
        }
        else
        {
            LblResultadoDb.TextColor =
                Colors.OrangeRed;

            LblResultadoDb.Text =
                "✗ " + resultado.Mensaje;
        }
    }

    private async void BtnExportar_Click(
        object sender,
        EventArgs e)
    {
        bool confirmar =
            await DisplayAlert(
                "Guardar cambios",
                "Se actualizará sccosmos.db en la carpeta sincronizada. ¿Continuar?",
                "Sí",
                "Cancelar");

        if (!confirmar)
            return;

        LblResultadoDb.Text =
            "Guardando cambios...";

        var resultado =
            await DatabaseSyncService.ExportarAsync();

        LblResultadoDb.TextColor =
            resultado.Correcto
                ? Colors.LightGreen
                : Colors.OrangeRed;

        LblResultadoDb.Text =
            resultado.Correcto
                ? "✓ " + resultado.Mensaje
                : "✗ " + resultado.Mensaje;
    }

    private void BtnProbarBase_Click(
        object sender,
        EventArgs e)
    {
        try
        {
            var resumen =
                DatabaseHelper.ObtenerResumenPrueba();

            LblResultadoDb.TextColor =
                Colors.LightGreen;

            LblResultadoDb.Text =
                $"✓ Base de datos abierta correctamente\n\n" +
                $"Nombre SC: {resumen.NombreSC}\n" +
                $"Personas: {resumen.Personas}\n" +
                $"Semanas: {resumen.Semanas}";
        }
        catch (Exception ex)
        {
            LblResultadoDb.TextColor =
                Colors.Red;

            LblResultadoDb.Text =
                $"Error al abrir la base de datos:\n{ex.Message}";
        }
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
