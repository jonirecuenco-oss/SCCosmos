using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Platforms.Android;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Alertas del sistema: calcula las alertas pendientes y las publica como
/// notificaciones del dispositivo (con el contador en el icono de la app en
/// Android). Se actualiza al arrancar, al abrir Inicio o Alertas, al marcar
/// las alertas como leídas y una vez al día de forma automática.
/// </summary>
public static class AlertasNotificacionService
{
    private static bool _permisoPedido;

    private static bool _programado;


    public static async Task ActualizarAsync()
    {
        try
        {
            // La esposa del SC no recibe notificaciones de ningún tipo:
            // se retiran las que pudiera haber y no se programa nada.
            if (PerfilAccesoService.Configurado
                && !PerfilAccesoService.EsHombre)
            {
                AlertasNotificacionAndroid.Actualizar(
                    0,
                    new List<string>());

                return;
            }

            await AsegurarPermisoAsync();

            List<AlertaMobile> alertas =
                await Task.Run(
                    () =>
                        AlertasMobileService.Calcular());

            var titulos =
                new List<string>();

            var destinos =
                new List<string>();

            foreach (AlertaMobile alerta in alertas)
            {
                string descripcion =
                    (alerta.Descripcion ?? "").Trim();

                titulos.Add(
                    string.IsNullOrWhiteSpace(descripcion)
                        ? alerta.Titulo
                        : $"{alerta.Titulo} — {descripcion}");

                // Al pulsar: la revisión de JW Hub lleva a Personas; el
                // resto abre la app en Inicio.
                destinos.Add(
                    string.Equals(
                        alerta.Tipo,
                        "JWHub",
                        StringComparison.OrdinalIgnoreCase)
                        ? "personas"
                        : "");
            }

            AlertasNotificacionAndroid.Actualizar(
                alertas.Count,
                titulos,
                destinos);
        }
        catch
        {
        }
    }


    /// <summary>
    /// En Android 13 o superior el usuario debe conceder el permiso de
    /// notificaciones; se pide la primera vez que se intenta mostrar.
    /// </summary>
    public static async Task AsegurarPermisoAsync()
    {
        if (_permisoPedido)
        {
            return;
        }

        _permisoPedido =
            true;

        try
        {
            PermissionStatus estado =
                await Permissions
                    .CheckStatusAsync<
                        Permissions.PostNotifications>();

            if (estado != PermissionStatus.Granted)
            {
                await Permissions
                    .RequestAsync<
                        Permissions.PostNotifications>();
            }
        }
        catch
        {
        }
    }


    /// <summary>Programa la revisión automática diaria (una sola vez).</summary>
    public static void ProgramarRevisionDiaria()
    {
        if (_programado)
        {
            return;
        }

        _programado =
            true;

        try
        {
            AlertasAlarmScheduler
                .ProgramarSiguiente();
        }
        catch
        {
        }
    }
}
