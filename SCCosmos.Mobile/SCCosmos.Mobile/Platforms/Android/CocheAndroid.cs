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

        // La línea del coche del widget, al día, y el aviso si ha entrado en reserva.
        SccosmosWidgetProvider.ActualizarCoche();
        RevisarAvisoCombustible();
    }

    // ---- Aviso de combustible bajo (página Avisos) ----

    private const string ClaveAvisado = "Coche_AvisoCombustibleEnviado";
    private const string CanalCombustible = "sccosmos_combustible";
    private const int IdAvisoCombustible = 47320;

    /// <summary>
    /// Si la última lectura (la de este móvil o la que ha llegado del otro) está por
    /// debajo del mínimo de Avisos, una notificación: una vez cada vez que baja, y
    /// otra después de repostar (cuando ha subido 5 puntos por encima). Como los
    /// demás avisos, solo en el móvil del SC.
    /// </summary>
    public static void RevisarAvisoCombustible()
    {
        try
        {
            if (Coche.Ultima(clave => DatabaseHelper.ObtenerConfiguracion(clave, "")) is not { Combustible: int combustible } lectura)
            {
                return;
            }

            var ajustes = AjustesAvisos.Leer(clave => DatabaseHelper.ObtenerConfiguracion(clave, ""));
            bool avisado = Preferences.Get(ClaveAvisado, false);

            if (combustible >= ajustes.CombustibleMinimo + 5)
            {
                if (avisado)
                {
                    Preferences.Set(ClaveAvisado, false);
                }

                return;
            }

            if (avisado
                || !Coche.EnReserva(lectura, ajustes.CombustibleMinimo)
                || !ajustes.CombustibleActivo
                || !PerfilAccesoService.EsHombre)
            {
                return;
            }

            NotificarCombustible(lectura, ajustes.CombustibleMinimo);
            Preferences.Set(ClaveAvisado, true);
        }
        catch (Exception ex)
        {
            RegistroErrores.Anotar(ex, "Coche.AvisoCombustible");
        }
    }

    private static void NotificarCombustible(Coche.Lectura lectura, int minimo)
    {
        Context contexto = global::Android.App.Application.Context;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            (contexto.GetSystemService(Context.NotificationService) as global::Android.App.NotificationManager)
                ?.CreateNotificationChannel(
                    new global::Android.App.NotificationChannel(CanalCombustible, "Combustible del coche", global::Android.App.NotificationImportance.High)
                    {
                        Description = "Cuando el combustible del coche baja del mínimo de Avisos"
                    });
        }

        string texto = $"Queda un {lectura.Combustible} % de combustible (avisa por debajo del {minimo} %)."
            + (lectura.Km is long km ? $" El coche marca {Coche.Km(km)}." : "");

        var notificacion = new AndroidX.Core.App.NotificationCompat.Builder(contexto, CanalCombustible);
        notificacion.SetSmallIcon(contexto.ApplicationInfo?.Icon ?? 0);
        notificacion.SetContentTitle("Combustible bajo: reserva");
        notificacion.SetContentText(texto);
        notificacion.SetStyle(new AndroidX.Core.App.NotificationCompat.BigTextStyle().BigText(texto));
        notificacion.SetPriority(AndroidX.Core.App.NotificationCompat.PriorityHigh);
        notificacion.SetAutoCancel(true);

        Intent? abrir = contexto.PackageManager?.GetLaunchIntentForPackage(contexto.PackageName ?? "");

        if (abrir is not null
            && global::Android.App.PendingIntent.GetActivity(
                   contexto,
                   IdAvisoCombustible,
                   abrir,
                   global::Android.App.PendingIntentFlags.Immutable | global::Android.App.PendingIntentFlags.UpdateCurrent)
               is { } alPulsar)
        {
            notificacion.SetContentIntent(alPulsar);
        }

        AndroidX.Core.App.NotificationManagerCompat.From(contexto)?.Notify(IdAvisoCombustible, notificacion.Build()!);
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
