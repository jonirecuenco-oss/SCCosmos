using System;
using System.Threading;
using System.Threading.Tasks;

using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using global::Android.OS;

using AndroidX.Core.App;

using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;


// =============================================================
// SERVICIO FOREGROUND PARA SINCRONIZAR JW HUB
// =============================================================

[Service(
    Exported = false,
    ForegroundServiceType =
        ForegroundService.TypeDataSync)]
public sealed class JwHubSyncForegroundService
    : Service
{
    // =========================================================
    // ACCIONES
    // =========================================================

    public const string ActionStartManual =
        "SCCosmos.Mobile.JWHUB_SYNC_MANUAL";

    public const string ActionStartAutomatic =
        "SCCosmos.Mobile.JWHUB_SYNC_AUTOMATIC";

    public const string ActionCancel =
        "SCCosmos.Mobile.JWHUB_SYNC_CANCEL";


    // =========================================================
    // ALIAS DE COMPATIBILIDAD
    //
    // Conservamos todos los nombres utilizados por
    // JwHubSyncController.
    // =========================================================

    public const string ActionManual =
        ActionStartManual;

    public const string ActionAutomatico =
        ActionStartAutomatic;

    public const string ActionCancelar =
        ActionCancel;


    public const string AccionManual =
        ActionStartManual;

    public const string AccionAutomatica =
        ActionStartAutomatic;

    public const string AccionCancelar =
        ActionCancel;


    public const string AccionIniciarManual =
        ActionStartManual;

    public const string AccionIniciarAutomatico =
        ActionStartAutomatic;


    // =========================================================
    // NOTIFICACIONES
    // =========================================================

    private const string NotificationChannelId =
        "sccosmos_jwhub_sync";

    private const string NotificationChannelName =
        "Sincronización JW Hub";


    // Canal separado para avisos que requieren atención.
    //
    // El canal de progreso sigue siendo LOW porque Android
    // obliga a mostrar una notificación mientras el servicio
    // foreground está trabajando.
    //
    // Este canal de resultados usa importancia DEFAULT para
    // que un conflicto / alta / baja pendiente quede visible.
    private const string ReviewNotificationChannelId =
        "sccosmos_jwhub_results_v1";

    private const string ReviewNotificationChannelName =
        "Resultados JW Hub";


    private const int NotificationId =
        7401;

    private const int FinalNotificationId =
        7402;

    private const int OpenAppRequestCode =
        7403;


    // =========================================================
    // ESTADO DEL SERVICIO
    // =========================================================

    private readonly object _bloqueo =
        new();


    private CancellationTokenSource?
        _cts;


    private bool
        _ejecutando;


    private PowerManager.WakeLock?
        _wakeLock;


    // =========================================================
    // ON CREATE
    // =========================================================

    public override void OnCreate()
    {
        base.OnCreate();


        CrearCanalNotificaciones();


        // Cada cambio de estado que produzca el extractor
        // se refleja también en la notificación Android.
        JwHubSyncState.Cambiado +=
            JwHubSyncState_Cambiado;
    }


    // =========================================================
    // ON START COMMAND
    // =========================================================

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        if (intent is null)
        {
            StopSelf();

            return StartCommandResult.NotSticky;
        }


        string accion =
            intent.Action ?? "";


        // =====================================================
        // CANCELACIÓN
        // =====================================================

        if (EsAccionCancelar(
                accion))
        {
            CancelarSintonizacion();

            return StartCommandResult.NotSticky;
        }


        // =====================================================
        // MANUAL / AUTOMÁTICA
        // =====================================================

        bool automatico =
            EsAccionAutomatica(
                accion);


        bool iniciar;


        lock (_bloqueo)
        {
            iniciar =
                !_ejecutando;


            if (iniciar)
            {
                _ejecutando =
                    true;


                _cts =
                    new CancellationTokenSource();
            }
        }


        // Ya hay otra sincronización ejecutándose.
        if (!iniciar)
        {
            return StartCommandResult.NotSticky;
        }


        try
        {
            // Android exige convertir rápidamente
            // el servicio en foreground.
            IniciarPrimerPlano(
                automatico
                    ? "Preparando sincronización automática..."
                    : "Preparando sincronización...",
                0,
                0,
                automatico);


            AdquirirWakeLock();


            JwHubSyncState.Establecer(
                EstadoJwHubSync.Preparando,
                0,
                0,
                automatico
                    ? "Preparando sincronización automática..."
                    : "Preparando JW Hub...",
                automatico);


            CancellationToken token;


            lock (_bloqueo)
            {
                token =
                    _cts?.Token
                    ?? CancellationToken.None;
            }


            // No bloqueamos OnStartCommand.
            _ =
                EjecutarAsync(
                    automatico,
                    token);
        }
        catch (Exception ex)
        {
            FinalizarInicioFallido(
                ex,
                automatico);
        }


        // Si Android mata este proceso no reiniciamos
        // automáticamente esta ejecución concreta.
        return StartCommandResult.NotSticky;
    }


    // =========================================================
    // EJECUTAR SINCRONIZACIÓN
    // =========================================================

    private async Task EjecutarAsync(
        bool automatico,
        CancellationToken token)
    {
        bool requiereSesion =
            false;

        bool huboError =
            false;


        bool hayRevisionPendiente =
            false;


        string mensajeRevisionPendiente =
            "";


        string mensajeFinal =
            "";


        int totalNombramientos =
            -1;


        try
        {
            token.ThrowIfCancellationRequested();


            // =================================================
            // 1. EXTRAER PERSONAS DE JW HUB
            // =================================================

            ResultadoSintonizacionJwHubAndroid resultado =
                await JwHubExtractorAndroid
                    .SintonizarTodasLasPersonasAsync(
                        token);


            token.ThrowIfCancellationRequested();


            // =================================================
            // 1b. NOMBRAMIENTOS DE JW HUB (espejo de Windows)
            //
            // La bandeja local de recomendaciones se reemplaza por lo
            // que Hub tenga ahora mismo. Un fallo aquí no debe
            // impedir la sincronización de personas.
            // =================================================

            try
            {
                JwHubSyncState.Establecer(
                    EstadoJwHubSync.Sintonizando,
                    0,
                    0,
                    "Extrayendo nombramientos de JW Hub...",
                    automatico);

                var recomendaciones =
                    await JwHubAccionesAndroid
                        .SintonizarNombramientosAsync();

                totalNombramientos =
                    recomendaciones.Count;

                await Task.Run(
                    () =>
                        SCCosmos.Mobile.Data.DatabaseHelper
                            .GuardarRecomendacionesNombramientosMobile(
                                recomendaciones),
                    token);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }


            // =================================================
            // 2. EXTRACCIÓN COMPLETA
            // =================================================

            if (resultado.ExtraccionCompleta)
            {
                // =============================================
                // COMPARAR CONTRA SQLITE
                //
                // Aquí todavía NO modificamos la base de datos.
                //
                // En modo de prueba limitado NO calculamos bajas.
                // Así una prueba de 3 personas solo muestra
                // cambios reales de esas 3 personas.
                // =============================================

                JwHubSyncState.Establecer(
                    EstadoJwHubSync.Sintonizando,
                    resultado.TotalPersonas,
                    resultado.TotalPersonas,
                    "Comparando con la base de datos...",
                    automatico);


                bool calcularBajas =
                    !resultado.EsPruebaLimitada;


                ResultadoAnalisisJwHub analisis =
                    JwHubSyncAnalysisService.Analizar(
                        resultado,
                        calcularBajas);


                hayRevisionPendiente =
                    analisis.TotalConflictos > 0
                    ||
                    analisis.TotalNuevos > 0
                    ||
                    analisis.TotalBajas > 0;


                if (hayRevisionPendiente)
                {
                    mensajeRevisionPendiente =
                        $"Conflictos: {analisis.TotalConflictos} · " +
                        $"Altas: {analisis.TotalNuevos} · " +
                        $"Posibles bajas: {analisis.TotalBajas}. " +
                        "Abre SCCosmos para revisar los cambios.";
                }


                string detalleConflictos =
    string.Join(
        " | ",
        analisis.Conflictos
            .Take(5)
            .Select(
                c =>
                    $"{c.NombreVisual}: " +
                    string.Join(
                        ", ",
                        c.CamposCambiados)));


                string resumen =
                    $"Extracción: " +
                    $"{resultado.TotalExtraidas}/" +
                    $"{resultado.TotalPersonas}. " +
                    $"{analisis.CrearResumen()}";


                if (resultado.EsPruebaLimitada)
                {
                    resumen +=
                        " · Modo de prueba: no se han " +
                        "calculado posibles bajas.";
                }


                if (!string.IsNullOrWhiteSpace(
                        detalleConflictos))
                {
                    resumen +=
                        $" · Detalle: {detalleConflictos}";
                }


                JwHubSyncState.Establecer(
                    EstadoJwHubSync.Completado,
                    resultado.TotalProcesadas,
                    resultado.TotalPersonas,
                    resumen,
                    automatico);


                ActualizarNotificacion(
                    "JW Hub comparado con SCCosmos",
                    resultado.TotalProcesadas,
                    resultado.TotalPersonas,
                    automatico);


                mensajeFinal =
                    resumen;

                if (totalNombramientos >= 0)
                {
                    mensajeFinal +=
                        $" · Nombramientos en Hub: " +
                        $"{totalNombramientos}.";
                }
            }

            // =================================================
            // 3. EXTRACCIÓN INCOMPLETA
            //
            // Puede ocurrir que, por ejemplo, se hayan
            // extraído correctamente 768 de 769 personas.
            //
            // En ese caso SÍ analizamos las personas válidas
            // para poder revisar conflictos, altas y cambios,
            // pero NO calculamos bajas.
            //
            // Esto evita que la persona que falló durante la
            // extracción aparezca falsamente como una baja.
            // =================================================

            else
            {
                huboError =
                    true;


                // =============================================
                // HAY DATOS PARCIALES UTILIZABLES
                // =============================================

                if (resultado.TotalExtraidas > 0)
                {
                    JwHubSyncState.Establecer(
                        EstadoJwHubSync.Sintonizando,
                        resultado.TotalProcesadas,
                        resultado.TotalPersonas,
                        "Comparando la extracción parcial " +
                        "con la base de datos...",
                        automatico);


                    ResultadoAnalisisJwHub analisis =
                        JwHubSyncAnalysisService.Analizar(
                            resultado,
                            calcularBajas: false);


                    hayRevisionPendiente =
                        analisis.TotalConflictos > 0
                        ||
                        analisis.TotalNuevos > 0
                        ||
                        analisis.TotalBajas > 0;


                    if (hayRevisionPendiente)
                    {
                        mensajeRevisionPendiente =
                            $"Conflictos: {analisis.TotalConflictos} · " +
                            $"Altas: {analisis.TotalNuevos} · " +
                            $"Posibles bajas: {analisis.TotalBajas}.";
                    }


                    string resumenError =
                        resultado.CrearResumen();


                    resumenError +=
                        $" {analisis.CrearResumen()} " +
                        "· Las posibles bajas NO se han " +
                        "calculado porque la extracción " +
                        "no fue completa.";


                    JwHubSyncState.Establecer(
                        EstadoJwHubSync.Error,
                        resultado.TotalProcesadas,
                        resultado.TotalPersonas,
                        resumenError,
                        automatico);


                    ActualizarNotificacion(
                        "JW Hub terminó con incidencias",
                        resultado.TotalProcesadas,
                        resultado.TotalPersonas,
                        automatico);


                    mensajeFinal =
                        resumenError;
                }

                // =============================================
                // NO SE PUDO EXTRAER NINGUNA PERSONA
                // =============================================

                else
                {
                    string resumenError =
                        resultado.CrearResumen();


                    JwHubSyncState.Establecer(
                        EstadoJwHubSync.Error,
                        resultado.TotalProcesadas,
                        resultado.TotalPersonas,
                        resumenError,
                        automatico);


                    ActualizarNotificacion(
                        "JW Hub terminó con incidencias",
                        resultado.TotalProcesadas,
                        resultado.TotalPersonas,
                        automatico);


                    mensajeFinal =
                        resumenError;
                }
            }
        }


        // =====================================================
        // CANCELACIÓN
        // =====================================================

        catch (System.OperationCanceledException)
        {
            JwHubSyncSnapshot estadoActual =
                JwHubSyncState.Actual;


            JwHubSyncState.Establecer(
                EstadoJwHubSync.Cancelado,
                estadoActual.Actual,
                estadoActual.Total,
                "Sincronización de JW Hub cancelada.",
                automatico);


            ActualizarNotificacion(
                "Sincronización cancelada",
                estadoActual.Actual,
                estadoActual.Total,
                automatico);


            mensajeFinal =
                "Sincronización de JW Hub cancelada.";
        }

        // =====================================================
        // SESIÓN JW HUB CADUCADA
        // =====================================================

        catch (JwHubSessionRequiredException ex)
        {
            requiereSesion =
                true;


            JwHubSyncSnapshot estadoActual =
                JwHubSyncState.Actual;


            JwHubSessionState
                .MarcarSesionNoValida();


            JwHubSyncState.Establecer(
                EstadoJwHubSync.RequiereInicioSesion,
                estadoActual.Actual,
                estadoActual.Total,
                "Es necesario iniciar sesión " +
                "de nuevo en JW Hub.",
                automatico);


            ActualizarNotificacion(
                "JW Hub necesita inicio de sesión",
                estadoActual.Actual,
                estadoActual.Total,
                automatico);


            mensajeFinal =
                string.IsNullOrWhiteSpace(
                    ex.Message)
                    ? "Es necesario iniciar sesión " +
                      "de nuevo en JW Hub."
                    : ex.Message;
        }

        // =====================================================
        // ERROR GENERAL
        // =====================================================

        catch (Exception ex)
        {
            huboError =
                true;


            JwHubSyncSnapshot estadoActual =
                JwHubSyncState.Actual;


            string mensaje =
                string.IsNullOrWhiteSpace(
                    ex.Message)
                    ? "Se produjo un error durante " +
                      "la sincronización de JW Hub."
                    : ex.Message;


            JwHubSyncState.Establecer(
                EstadoJwHubSync.Error,
                estadoActual.Actual,
                estadoActual.Total,
                mensaje,
                automatico);


            ActualizarNotificacion(
                "Error al sincronizar JW Hub",
                estadoActual.Actual,
                estadoActual.Total,
                automatico);


            mensajeFinal =
                mensaje;
        }

        // =====================================================
        // LIMPIEZA
        // =====================================================

        finally
        {
            CancellationTokenSource?
                ctsParaLiberar;


            lock (_bloqueo)
            {
                ctsParaLiberar =
                    _cts;


                _cts =
                    null;


                _ejecutando =
                    false;
            }


            try
            {
                ctsParaLiberar?
                    .Dispose();
            }
            catch
            {
            }


            LiberarWakeLock();


            DetenerPrimerPlano();


            // =================================================
            // NOTIFICACIÓN FINAL
            //
            // Android obliga a mostrar la notificación foreground
            // mientras trabaja el servicio. DetenerPrimerPlano()
            // la elimina al terminar.
            //
            // Después de terminar SOLO dejamos una notificación si:
            //
            // - hay conflictos / altas / posibles bajas;
            // - hubo una incidencia o error;
            // - la sesión de JW Hub necesita renovarse.
            //
            // Si todo coincide, no queda ninguna notificación.
            // =================================================

            if (requiereSesion)
            {
                PublicarNotificacionFinal(
                    "JW Hub necesita inicio de sesión",
                    string.IsNullOrWhiteSpace(
                        mensajeFinal)
                        ? "Abre SCCosmos y renueva " +
                          "la sesión de JW Hub."
                        : mensajeFinal);
            }
            else if (huboError)
            {
                string texto =
                    string.IsNullOrWhiteSpace(
                        mensajeFinal)
                        ? "La sincronización de JW Hub " +
                          "terminó con incidencias."
                        : mensajeFinal;


                if (hayRevisionPendiente
                    &&
                    !string.IsNullOrWhiteSpace(
                        mensajeRevisionPendiente))
                {
                    texto +=
                        " " +
                        mensajeRevisionPendiente;
                }


                PublicarNotificacionFinal(
                    "Sincronización JW Hub con incidencias",
                    texto);
            }
            else if (hayRevisionPendiente)
            {
                PublicarNotificacionFinal(
                    "Cambios pendientes en JW Hub",
                    mensajeRevisionPendiente);
            }


            StopSelf();
        }
    }


    // =========================================================
    // CANCELAR SINCRONIZACIÓN
    // =========================================================

    private void CancelarSintonizacion()
    {
        bool habiaProceso;


        lock (_bloqueo)
        {
            habiaProceso =
                _ejecutando;


            if (_cts is not null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch
                {
                }
            }
        }


        if (!habiaProceso)
        {
            StopSelf();

            return;
        }


        JwHubSyncSnapshot actual =
            JwHubSyncState.Actual;


        JwHubSyncState.Establecer(
            EstadoJwHubSync.Cancelando,
            actual.Actual,
            actual.Total,
            "Cancelando sincronización de JW Hub...",
            actual.Automatico);


        ActualizarNotificacion(
            "Cancelando sincronización...",
            actual.Actual,
            actual.Total,
            actual.Automatico);
    }


    // =========================================================
    // CAMBIO DE ESTADO GLOBAL
    //
    // Refleja en la notificación Android el progreso
    // que va enviando JwHubExtractorAndroid.
    // =========================================================

    private void JwHubSyncState_Cambiado()
    {
        try
        {
            JwHubSyncSnapshot estado =
                JwHubSyncState.Actual;


            if (!_ejecutando)
            {
                return;
            }


            string mensaje =
                string.IsNullOrWhiteSpace(
                    estado.Mensaje)
                    ? "Sincronizando JW Hub..."
                    : estado.Mensaje;


            ActualizarNotificacion(
                mensaje,
                estado.Actual,
                estado.Total,
                estado.Automatico);
        }
        catch
        {
            // Un fallo visual en la notificación
            // nunca debe detener la sincronización.
        }
    }


    // =========================================================
    // DETECTAR ACCIÓN CANCELAR
    // =========================================================

    private static bool EsAccionCancelar(
        string accion)
    {
        if (string.IsNullOrWhiteSpace(
                accion))
        {
            return false;
        }


        return
            accion.Equals(
                ActionCancel,
                StringComparison.OrdinalIgnoreCase)
            ||
            accion.Contains(
                "CANCEL",
                StringComparison.OrdinalIgnoreCase);
    }


    // =========================================================
    // DETECTAR ACCIÓN AUTOMÁTICA
    // =========================================================

    private static bool EsAccionAutomatica(
        string accion)
    {
        if (string.IsNullOrWhiteSpace(
                accion))
        {
            return false;
        }


        return
            accion.Equals(
                ActionStartAutomatic,
                StringComparison.OrdinalIgnoreCase)
            ||
            accion.Contains(
                "AUTO",
                StringComparison.OrdinalIgnoreCase);
    }


    // =========================================================
    // INICIAR FOREGROUND
    // =========================================================

    private void IniciarPrimerPlano(
        string mensaje,
        int actual,
        int total,
        bool automatico)
    {
        Notification notificacion =
            CrearNotificacion(
                mensaje,
                actual,
                total,
                automatico,
                ongoing: true);


        if (Build.VERSION.SdkInt >=
            BuildVersionCodes.Q)
        {
            StartForeground(
                NotificationId,
                notificacion,
                ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(
                NotificationId,
                notificacion);
        }
    }


    // =========================================================
    // ACTUALIZAR NOTIFICACIÓN
    // =========================================================

    private void ActualizarNotificacion(
        string mensaje,
        int actual,
        int total,
        bool automatico)
    {
        try
        {
            Notification notificacion =
                CrearNotificacion(
                    mensaje,
                    actual,
                    total,
                    automatico,
                    ongoing: true);


            NotificationManager? manager =
                GetSystemService(
                    NotificationService)
                as NotificationManager;


            manager?.Notify(
                NotificationId,
                notificacion);
        }
        catch
        {
            // Nunca detenemos la sincronización por
            // un problema al actualizar la notificación.
        }
    }


    // =========================================================
    // CREAR NOTIFICACIÓN FOREGROUND
    // =========================================================

    private Notification CrearNotificacion(
        string mensaje,
        int actual,
        int total,
        bool automatico,
        bool ongoing)
    {
        string titulo =
            automatico
                ? "SCCosmos · JW Hub automático"
                : "SCCosmos · JW Hub";


        var builder =
            new NotificationCompat.Builder(
                this,
                NotificationChannelId)
                .SetSmallIcon(
                    ApplicationInfo.Icon)
                .SetContentTitle(
                    titulo)
                .SetContentText(
                    mensaje)
                .SetStyle(
                    new NotificationCompat
                        .BigTextStyle()
                        .BigText(
                            mensaje))
                .SetOngoing(
                    ongoing)
                .SetOnlyAlertOnce(
                    true)
                .SetAutoCancel(
                    false)
                .SetCategory(
                    NotificationCompat
                        .CategoryService)
                .SetPriority(
                    NotificationCompat
                        .PriorityLow);


        PendingIntent? abrirApp =
            CrearPendingIntentAbrirApp();


        if (abrirApp is not null)
        {
            builder.SetContentIntent(
                abrirApp);
        }


        if (total > 0)
        {
            int progreso =
                Math.Clamp(
                    actual,
                    0,
                    total);


            builder.SetProgress(
                total,
                progreso,
                false);
        }
        else if (ongoing)
        {
            builder.SetProgress(
                0,
                0,
                true);
        }
        else
        {
            builder.SetProgress(
                0,
                0,
                false);
        }


        return builder.Build();
    }


    // =========================================================
    // NOTIFICACIÓN FINAL
    // =========================================================

    private void PublicarNotificacionFinal(
        string titulo,
        string mensaje)
    {
        try
        {
            var builder =
                new NotificationCompat.Builder(
                    this,
                    ReviewNotificationChannelId)
                    .SetSmallIcon(
                        ApplicationInfo.Icon)
                    .SetContentTitle(
                        titulo)
                    .SetContentText(
                        mensaje)
                    .SetStyle(
                        new NotificationCompat
                            .BigTextStyle()
                            .BigText(
                                mensaje))
                    .SetOngoing(
                        false)
                    .SetAutoCancel(
                        true)
                    .SetPriority(
                        NotificationCompat
                            .PriorityDefault);


            PendingIntent? abrirApp =
                CrearPendingIntentAbrirApp();


            if (abrirApp is not null)
            {
                builder.SetContentIntent(
                    abrirApp);
            }


            NotificationManager? manager =
                GetSystemService(
                    NotificationService)
                as NotificationManager;


            manager?.Notify(
                FinalNotificationId,
                builder.Build());
        }
        catch
        {
        }
    }


    // =========================================================
    // ABRIR SCCOSMOS DESDE LA NOTIFICACIÓN
    // =========================================================

    private PendingIntent?
        CrearPendingIntentAbrirApp()
    {
        try
        {
            Intent? intent =
                PackageManager?
                    .GetLaunchIntentForPackage(
                        PackageName);


            if (intent is null)
            {
                return null;
            }


            intent.AddFlags(
                ActivityFlags.ClearTop |
                ActivityFlags.SingleTop);


            PendingIntentFlags flags =
                PendingIntentFlags.UpdateCurrent;


            if (Build.VERSION.SdkInt >=
                BuildVersionCodes.M)
            {
                flags |=
                    PendingIntentFlags.Immutable;
            }


            return PendingIntent.GetActivity(
                this,
                OpenAppRequestCode,
                intent,
                flags);
        }
        catch
        {
            return null;
        }
    }


    // =========================================================
    // CANAL DE NOTIFICACIONES
    // =========================================================

    private void CrearCanalNotificaciones()
    {
        if (Build.VERSION.SdkInt <
            BuildVersionCodes.O)
        {
            return;
        }


        try
        {
            var canalProgreso =
                new NotificationChannel(
                    NotificationChannelId,
                    NotificationChannelName,
                    NotificationImportance.Low)
                {
                    Description =
                        "Progreso de la sincronización " +
                        "de datos con JW Hub."
                };


            canalProgreso.SetShowBadge(
                false);


            var canalResultados =
                new NotificationChannel(
                    ReviewNotificationChannelId,
                    ReviewNotificationChannelName,
                    NotificationImportance.Default)
                {
                    Description =
                        "Avisos cuando la sincronización de " +
                        "JW Hub detecta cambios o incidencias."
                };


            canalResultados.SetShowBadge(
                true);


            NotificationManager? manager =
                GetSystemService(
                    NotificationService)
                as NotificationManager;


            manager?
                .CreateNotificationChannel(
                    canalProgreso);


            manager?
                .CreateNotificationChannel(
                    canalResultados);
        }
        catch
        {
        }
    }


    // =========================================================
    // WAKE LOCK
    //
    // Mantiene activa la CPU mientras trabaja
    // el servicio foreground.
    // =========================================================

    private void AdquirirWakeLock()
    {
        try
        {
            if (_wakeLock?.IsHeld ==
                true)
            {
                return;
            }


            PowerManager? powerManager =
                GetSystemService(
                    PowerService)
                as PowerManager;


            if (powerManager is null)
            {
                return;
            }


            _wakeLock =
                powerManager.NewWakeLock(
                    WakeLockFlags.Partial,
                    $"{PackageName}:JwHubSync");


            _wakeLock.SetReferenceCounted(
                false);


            // Máximo de seguridad:
            // seis horas.
            _wakeLock.Acquire(
                6L *
                60L *
                60L *
                1000L);
        }
        catch
        {
            _wakeLock =
                null;
        }
    }


    // =========================================================
    // LIBERAR WAKE LOCK
    // =========================================================

    private void LiberarWakeLock()
    {
        try
        {
            if (_wakeLock?.IsHeld ==
                true)
            {
                _wakeLock.Release();
            }
        }
        catch
        {
        }
        finally
        {
            try
            {
                _wakeLock?
                    .Dispose();
            }
            catch
            {
            }


            _wakeLock =
                null;
        }
    }


    // =========================================================
    // DETENER FOREGROUND
    // =========================================================

    private void DetenerPrimerPlano()
    {
        try
        {
            if (Build.VERSION.SdkInt >=
                BuildVersionCodes.N)
            {
                StopForeground(
                    StopForegroundFlags.Remove);
            }
            else
            {
#pragma warning disable CS0618

                StopForeground(
                    true);

#pragma warning restore CS0618
            }
        }
        catch
        {
        }
    }


    // =========================================================
    // FALLO DURANTE EL ARRANQUE
    // =========================================================

    private void FinalizarInicioFallido(
        Exception ex,
        bool automatico)
    {
        lock (_bloqueo)
        {
            _ejecutando =
                false;


            try
            {
                _cts?
                    .Dispose();
            }
            catch
            {
            }


            _cts =
                null;
        }


        LiberarWakeLock();


        string mensaje =
            string.IsNullOrWhiteSpace(
                ex.Message)
                ? "No se pudo iniciar la " +
                  "sincronización de JW Hub."
                : ex.Message;


        JwHubSyncState.Establecer(
            EstadoJwHubSync.Error,
            0,
            0,
            mensaje,
            automatico);


        DetenerPrimerPlano();


        StopSelf();
    }


    // =========================================================
    // ON BIND
    // =========================================================

    public override IBinder?
        OnBind(
            Intent? intent)
    {
        return null;
    }


    // =========================================================
    // ON DESTROY
    // =========================================================

    public override void OnDestroy()
    {
        JwHubSyncState.Cambiado -=
            JwHubSyncState_Cambiado;


        lock (_bloqueo)
        {
            try
            {
                _cts?
                    .Cancel();
            }
            catch
            {
            }
        }


        LiberarWakeLock();


        base.OnDestroy();
    }
}