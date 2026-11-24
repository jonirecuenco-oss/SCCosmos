using System.Text;
using Android.Bluetooth;
using Android.Content;
using Microsoft.Maui.ApplicationModel;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// El permiso de «Dispositivos cercanos» (desde Android 12), que hace falta
/// para conectarse al OBDLink; antes bastaba con el de Bluetooth.
/// </summary>
public sealed class PermisoBluetoothObd : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { (global::Android.Manifest.Permission.BluetoothConnect, true) }
            : new[] { (global::Android.Manifest.Permission.Bluetooth, false) };
}

/// <summary>
/// La prueba del coche: se conecta al OBDLink, lo prepara y le pregunta las
/// órdenes de <see cref="PruebaCoche"/>. Devuelve el informe para enseñarlo.
/// Se llama fuera del hilo de la pantalla.
/// </summary>
public static class PruebaCocheAndroid
{
    public static string Ejecutar(Action<string> progreso)
    {
        var informe = new StringBuilder();
        informe.AppendLine($"Prueba del coche · {DateTime.Now:dd/MM/yyyy HH:mm} · SCCosmos {Services.ActualizacionMobileService.VersionInstalada}");

        var gestor = global::Android.App.Application.Context.GetSystemService(Context.BluetoothService) as BluetoothManager;
        BluetoothAdapter? adaptador = gestor?.Adapter;

        if (adaptador is null)
        {
            return Fin(informe, "Este móvil no tiene Bluetooth.");
        }

        if (!adaptador.IsEnabled)
        {
            return Fin(informe, "El Bluetooth está apagado: enciéndelo y vuelve a probar.");
        }

        BluetoothDevice? obd = ConexionObd.Buscar(adaptador, out string emparejados);

        if (obd is null)
        {
            return Fin(informe, "No encuentro el OBDLink entre los dispositivos Bluetooth emparejados"
                + (emparejados.Length > 0 ? $" (están: {emparejados})." : "."));
        }

        progreso($"Conectando con {obd.Name}…");

        ConexionObd conexion;

        try
        {
            conexion = ConexionObd.Conectar(obd);
        }
        catch (Exception)
        {
            return Fin(informe, $"No he podido conectar con {obd.Name}. Si la app de OBDLink está abierta o conectada, "
                + "ciérrala (o desconéctala) y vuelve a probar. El coche tiene que tener el contacto puesto.");
        }

        using (conexion)
        {
            progreso("Preparando el OBDLink…");

            foreach (string orden in PruebaCoche.Preparacion)
            {
                if (conexion.Enviar(orden, orden == "ATWS" ? 8000 : 3000) is null)
                {
                    return Fin(informe, $"El OBDLink no responde ({orden}).");
                }
            }

            string adaptadorTexto = string.Join(" · ",
                new[] { obd.Name ?? "", Plano(conexion.Enviar("ATI")), Plano(conexion.Enviar("STI")), Plano(conexion.Enviar("ATRV")) }
                    .Where(t => t.Length > 0 && t != "?"));

            informe.AppendLine("Adaptador: " + adaptadorTexto);

            // La primera pregunta al coche busca el protocolo: puede tardar.
            progreso("Buscando cómo habla el coche…");

            string primera = PruebaCoche.OrdenesSoportados[0];
            string? respuesta = conexion.Enviar(primera, 20000);
            var pids = PruebaCoche.PidsSoportados(primera, respuesta).ToList();

            if (pids.Count == 0)
            {
                return Fin(informe, "El coche no responde: ¿tiene el contacto puesto? Respuesta: "
                    + (respuesta is null ? "ninguna" : Plano(respuesta)));
            }

            informe.AppendLine("Protocolo: " + PruebaCoche.Protocolo(conexion.Enviar("ATDPN")));

            foreach (string orden in PruebaCoche.OrdenesSoportados.Skip(1))
            {
                pids.AddRange(PruebaCoche.PidsSoportados(orden, conexion.Enviar(orden)));
            }

            informe.AppendLine("Datos estándar que da el coche: " + PruebaCoche.TextoPids(pids));

            bool alMotor = false;
            string grupo = "";
            int numero = 0;

            foreach (PruebaCoche.Orden orden in PruebaCoche.Ordenes)
            {
                progreso($"Probando {++numero} de {PruebaCoche.Ordenes.Count}…");

                if (orden.Motor != alMotor)
                {
                    conexion.Enviar(orden.Motor ? "ATSH7E0" : "ATSH7DF", 3000);
                    alMotor = orden.Motor;
                }

                string? recibido = conexion.Enviar(orden.Peticion);

                if (recibido is null)
                {
                    return Fin(informe, $"El OBDLink ha dejado de responder ({orden.Peticion}).");
                }

                if (orden.Grupo != grupo)
                {
                    grupo = orden.Grupo;
                    informe.AppendLine().AppendLine(grupo.ToUpperInvariant());
                }

                informe.AppendLine(PruebaCoche.Linea(orden, recibido));
            }

            // Se deja el OBDLink como estaba, para su app.
            conexion.Enviar("ATD", 3000);
        }

        return informe.ToString().TrimEnd();
    }

    private static string Plano(string? recibido) => PruebaCoche.Limpiar(recibido).Replace("\n", " | ");

    private static string Fin(StringBuilder informe, string motivo) =>
        informe.AppendLine().AppendLine(motivo).ToString().TrimEnd();
}
