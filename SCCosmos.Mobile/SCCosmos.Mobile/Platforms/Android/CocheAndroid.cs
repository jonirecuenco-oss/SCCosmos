using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Storage;
using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Services;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// La lectura del coche por el OBDLink: el combustible (01 2F) y el contador de
/// km (01 31), que se guardan en Configuración con <see cref="Coche"/>. Se lee
/// al conectarse el móvil al Bluetooth del coche (ReceptorCoche y ServicioCoche)
/// o con «Leer ahora» en Configuración.
/// </summary>
public static class CocheAndroid
{
    // El Bluetooth del coche (la radio): al conectarse, se lee el OBDLink. Cada móvil, el suyo.
    private const string ClaveDireccion = "Coche_BluetoothDireccion";
    private const string ClaveNombre = "Coche_BluetoothNombre";

    private static readonly object Candado = new();

    public static string DireccionCoche => Preferences.Get(ClaveDireccion, "") ?? "";

    public static string NombreCoche => Preferences.Get(ClaveNombre, "") ?? "";

    public static void ElegirCoche(string direccion, string nombre)
    {
        Preferences.Set(ClaveDireccion, direccion ?? "");
        Preferences.Set(ClaveNombre, nombre ?? "");
    }

    /// <summary>El permiso de «Dispositivos cercanos» (desde Android 12).</summary>
    public static bool PermisoConcedido =>
        !OperatingSystem.IsAndroidVersionAtLeast(31)
        || global::Android.App.Application.Context.CheckSelfPermission(
               global::Android.Manifest.Permission.BluetoothConnect) == Permission.Granted;

    public static BluetoothAdapter? Adaptador =>
        (global::Android.App.Application.Context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;

    /// <summary>Los dispositivos emparejados que pueden ser el coche (todos menos el OBDLink).</summary>
    public static List<(string Direccion, string Nombre)> Emparejados() =>
        (Adaptador?.BondedDevices?.ToList() ?? new List<BluetoothDevice>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Address)
                        && !(d.Name ?? "").Contains("OBD", StringComparison.OrdinalIgnoreCase))
            .Select(d => (d.Address!, string.IsNullOrWhiteSpace(d.Name) ? d.Address! : d.Name!))
            .OrderBy(d => d.Item2, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Prepara el OBDLink y comprueba que el coche responde.</summary>
    public static bool Preparar(ConexionObd conexion)
    {
        foreach (string orden in PruebaCoche.Preparacion)
        {
            if (conexion.Enviar(orden, orden == "ATWS" ? 8000 : 3000) is null)
            {
                return false;
            }
        }

        // La primera pregunta al coche busca el protocolo: puede tardar.
        return PruebaCoche.PidsSoportados("0100", conexion.Enviar("0100", 20000)).Any();
    }

    /// <summary>Lee el combustible y el contador y, si ha llegado algo, lo guarda.</summary>
    public static bool LeerYGuardar(ConexionObd conexion)
    {
        int? combustible = Coche.CombustibleDe(conexion.Enviar("012F"));
        int? contador = Coche.ContadorDe(conexion.Enviar("0131"));

        if (combustible is null && contador is null)
        {
            return false;
        }

        Guardar(combustible, contador);
        return true;
    }

    private static void Guardar(int? combustible, int? contador)
    {
        lock (Candado)
        {
            var anterior = Coche.LecturaDe(DatabaseHelper.ObtenerConfiguracion(Coche.ClaveLectura, ""));
            var baseActual = Coche.BaseDe(DatabaseHelper.ObtenerConfiguracion(Coche.ClaveBase, ""));
            var puestos = Coche.KmPuestosDe(DatabaseHelper.ObtenerConfiguracion(Coche.ClaveKmPuestos, ""));

            string quien = PerfilAccesoService.Configurado && !PerfilAccesoService.EsHombre ? "Esposa" : "SC";

            var (lectura, nuevaBase) = Coche.Aplicar(DateTime.Now, quien, combustible, contador, anterior, baseActual, puestos);

            DatabaseHelper.GuardarConfiguracion(Coche.ClaveLectura, Coche.Json(lectura));

            if (nuevaBase is not null && nuevaBase != baseActual)
            {
                DatabaseHelper.GuardarConfiguracion(Coche.ClaveBase, Coche.Json(nuevaBase));
            }
        }

        // La línea del coche del widget, al día.
        SccosmosWidgetProvider.ActualizarCoche();
    }

    /// <summary>
    /// Una lectura suelta («Leer ahora» o, si Android no deja arrancar el servicio,
    /// al subir al coche): se conecta, lee, guarda y se desconecta. Devuelve ""
    /// si ha ido bien y, si no, por qué.
    /// </summary>
    public static string LeerUnaVez()
    {
        if (ServicioCoche.EnMarcha)
        {
            return "Ya se está leyendo: el móvil está conectado al coche.";
        }

        if (!PermisoConcedido)
        {
            return "Falta el permiso de «Dispositivos cercanos».";
        }

        if (Adaptador is not { IsEnabled: true } adaptador)
        {
            return "El Bluetooth está apagado.";
        }

        if (ConexionObd.Buscar(adaptador, out _) is not { } obd)
        {
            return "No encuentro el OBDLink entre los dispositivos Bluetooth emparejados.";
        }

        ConexionObd conexion;

        try
        {
            conexion = ConexionObd.Conectar(obd);
        }
        catch (Exception)
        {
            return "No he podido conectar con el OBDLink. Si la app de OBDLink está conectada, ciérrala; el coche tiene que tener el contacto puesto.";
        }

        using (conexion)
        {
            if (!Preparar(conexion))
            {
                return "El coche no responde: ¿tiene el contacto puesto?";
            }

            bool leido = LeerYGuardar(conexion);

            // Se deja el OBDLink como estaba, para su app.
            conexion.Enviar("ATD", 3000);

            return leido ? "" : "El coche no ha dado los datos.";
        }
    }
}
