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
            // La tabla de tareas debe existir antes de crear los
            // disparadores de fusión (que detectan las tablas nuevas).
            DatabaseHelper.AsegurarTablaTareasMobile();

            // Tabla del registro de actividad (historial de acciones).
            DatabaseHelper.AsegurarTablaRegistroActividadMobile();

            // Tablas de escuelas (clases, estudiantes, asientos y
            // limpieza): deben existir antes de crear los disparadores.
            DatabaseHelper.AsegurarTablasEscuelasClasesMobile();

            // Programa de la escuela (talleres y horario).
            DatabaseHelper.AsegurarTablasEscuelasProgramaMobile();

            // Reuniones para el Servicio del Campo (RSC).
            DatabaseHelper.AsegurarTablasRscMobile();

            // Sincronización automática de la base de datos:
            // 1) intenta importar si el PC cambió la BD y no hay
            //    cambios locales pendientes;
            // 2) arranca el vigilante que exporta los cambios locales.
            _ = SincronizarAlArrancarAsync();

            AutoSyncService.Iniciar();

            // Motor nuevo: sincronización directa entre dispositivos
            // (sin Syncthing para la base de datos).
            SincronizacionMobile.Iniciar();

            // Notificaciones del sistema: revisión automática diaria y
            // actualización inmediata del contador de alertas.
            AlertasNotificacionService
                .ProgramarRevisionDiaria();

            // Avisos de tareas: alarma para el siguiente aviso (empezar el
            // día, la hora de la tarea o su aviso previo).
            TareasAvisoService
                .ProgramarSiguiente();

            _ = AlertasNotificacionService
                .ActualizarAsync();

            _ = TareasAvisoService
                .VerificarAsync();

            // Widget del escritorio: programación de hoy y próximas tareas.
            SCCosmos.Mobile.Platforms.Android
                .SccosmosWidgetProvider
                .ActualizarTodos();

            var shell =
                new AppShell();

            // Refresco automático de la página abierta cuando llegan
            // cambios de otro dispositivo.
            ActualizacionPaginasMobile.Iniciar(
                shell);

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
