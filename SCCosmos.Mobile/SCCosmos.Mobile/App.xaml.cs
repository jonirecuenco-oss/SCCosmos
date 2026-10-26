using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // La interfaz está diseñada en tema oscuro. Forzarlo evita
            // que, con el móvil en modo claro, los controles sin color
            // explícito queden con texto oscuro sobre fondos oscuros.
            UserAppTheme =
                AppTheme.Dark;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var arranque = System.Diagnostics.Stopwatch.StartNew();

            // La tabla de tareas debe existir antes de crear los
            // disparadores de fusión (que detectan las tablas nuevas).
            DatabaseHelper.AsegurarTablaTareasMobile();
            DatabaseHelper.AsegurarTablaAlertasLeidasMobile();
            DatabaseHelper.AsegurarTablaHorasMesMobile();

            // Tabla del registro de actividad (historial de acciones).
            DatabaseHelper.AsegurarTablaRegistroActividadMobile();

            // Tablas de escuelas (clases, estudiantes, asientos y
            // limpieza): deben existir antes de crear los disparadores.
            DatabaseHelper.AsegurarTablasEscuelasClasesMobile();

            // Programa de la escuela (talleres y horario).
            DatabaseHelper.AsegurarTablasEscuelasProgramaMobile();

            // Reuniones para el Servicio del Campo (RSC).
            DatabaseHelper.AsegurarTablasRscMobile();

            Perf.Registrar("Arranque.Tablas", arranque.ElapsedMilliseconds);

            // Sincronización automática de la base de datos:
            // 1) intenta importar si el PC cambió la BD y no hay
            //    cambios locales pendientes;
            // 2) arranca el vigilante que exporta los cambios locales.
            // La sincronización (registro de cambios, disparadores y
            // motor) tarda ~1,4 s: se hace en segundo plano para que la
            // app abra de inmediato.
            _ = Task.Run(async () =>
            {
                await Task.Delay(800);

                var relojSync =
                    System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    await SincronizarAlArrancarAsync();
                }
                catch
                {
                }

                try
                {
                    AutoSyncService.Iniciar();
                }
                catch
                {
                }

                try
                {
                    SincronizacionMobile.Iniciar();
                }
                catch
                {
                }

                Perf.Registrar(
                    "Arranque.SyncDiferido",
                    relojSync.ElapsedMilliseconds);
            });

            // Notificaciones del sistema: revisión automática diaria y
            // actualización inmediata del contador de alertas.
            AlertasNotificacionService
                .ProgramarRevisionDiaria();
            Perf.Registrar("Arranque.Alertas", arranque.ElapsedMilliseconds);

            // Avisos de tareas: alarma para el siguiente aviso (empezar el
            // día, la hora de la tarea o su aviso previo).
            TareasAvisoService
                .ProgramarSiguiente();
            Perf.Registrar("Arranque.Tareas", arranque.ElapsedMilliseconds);

            _ = AlertasNotificacionService
                .ActualizarAsync();

            _ = TareasAvisoService
                .VerificarAsync();
            Perf.Registrar("Arranque.FinServicios", arranque.ElapsedMilliseconds);

            Perf.Registrar("Arranque.Servicios", arranque.ElapsedMilliseconds);

            // Widget del escritorio: programación de hoy y próximas tareas.
            // Se actualiza en segundo plano para no retrasar el arranque.
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);

                try
                {
                    SCCosmos.Mobile.Platforms.Android
                        .SccosmosWidgetProvider
                        .ActualizarTodos();

                    Perf.Registrar("Arranque.Widget", arranque.ElapsedMilliseconds);
                }
                catch
                {
                }
            });

            var shell =
                new AppShell();

            // Refresco automático de la página abierta cuando llegan
            // cambios de otro dispositivo.
            ActualizacionPaginasMobile.Iniciar(
                shell);

            Perf.Registrar("Arranque.Shell", arranque.ElapsedMilliseconds);

            // Selección de perfil en el primer arranque.
            _ = SolicitarPerfilAsync(shell);

            return new Window(shell);
        }

        private static async Task SolicitarPerfilAsync(
            AppShell shell)
        {
            try
            {
                await Task.Delay(800);

                await shell.SolicitarPerfilSiProcedeAsync();
            }
            catch
            {
            }
        }

        private static async Task SincronizarAlArrancarAsync()
        {
            try
            {
                await AutoSyncService.ImportarSiHayCambiosAsync();
            }
            catch
            {
            }
        }

        protected override void OnSleep()
        {
            // Al salir a segundo plano (o cerrar), se envían AHORA los
            // cambios pendientes: a los dispositivos vinculados y a la
            // carpeta compartida. Se espera un momento a que termine
            // para que no se quede nada sin propagar.
            try
            {
                SincronizacionMobile
                    .VaciarPendientesSiHaceFaltaAsync()
                    .Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
            }

            // Sistema antiguo de carpeta (solo si siguiera activo).
            _ = AutoSyncService.SincronizarSiHayCambiosAsync();

            // Se reprograma el próximo aviso de tareas con lo último que
            // haya guardado (por si se tocaron tareas hace un momento).
            TareasAvisoService
                .ProgramarSiguiente();

            // El widget se refresca con lo último guardado.
            SCCosmos.Mobile.Platforms.Android
                .SccosmosWidgetProvider
                .ActualizarTodos();

            base.OnSleep();
        }
    }
}
