using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Mientras el móvil está conectado al Bluetooth del coche: se conecta al OBDLink
/// y lee el combustible y el contador de km al empezar y cada 2 minutos. Para al
/// desconectarse el coche, si el coche deja de responder (motor parado) o a las
/// 8 horas.
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class ServicioCoche : Service
{
    private const int IdNotificacion = 47310;
    private const string Canal = "sccosmos_coche";

    private static CancellationTokenSource? _parar;

    public static bool EnMarcha => _parar is not null;

    public static void Parar() => _parar?.Cancel();

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Lo primero, en primer plano: Android lo exige enseguida.
        try
        {
            CrearCanal();

            var constructor = new NotificationCompat.Builder(this, Canal);
            constructor.SetSmallIcon(ApplicationInfo!.Icon);
            constructor.SetContentTitle("SCCosmos · Coche");
            constructor.SetContentText("Leyendo el combustible y los km por el OBDLink.");
            constructor.SetOngoing(true);
            constructor.SetSilent(true);

            Notification notificacion = constructor.Build()!;

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(IdNotificacion, notificacion, ForegroundService.TypeConnectedDevice);
            }
            else
            {
                StartForeground(IdNotificacion, notificacion);
            }
        }
        catch (Exception ex)
        {
            RegistroErrores.Anotar(ex, "Coche.Servicio");
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        // Con la app cerrada, la sincronización no está en marcha: así llegan
        // durante el viaje los km de otro móvil y salen las lecturas de este.
        try
        {
            SincronizacionMobile.Iniciar();
        }
        catch (Exception ex)
        {
            RegistroErrores.Anotar(ex, "Coche.Sincro");
        }

        if (_parar is null)
        {
            var parar = new CancellationTokenSource();
            _parar = parar;

            Task.Run(() => Bucle(parar.Token)).ContinueWith(_ => Terminar(parar));
        }

        return StartCommandResult.NotSticky;
    }

    private static void Bucle(CancellationToken parar)
    {
        DateTime fin = DateTime.Now.AddHours(8);
        ConexionObd? conexion = null;

        try
        {
            // El OBDLink se despierta con el coche: unos segundos y, si no, se reintenta
            // (2 minutos: si la app de OBDLink lo tiene, no hay nada que hacer).
            for (int intento = 0; conexion is null && intento < 6; intento++)
            {
                if (parar.WaitHandle.WaitOne(TimeSpan.FromSeconds(intento == 0 ? 5 : 20)))
                {
                    return;
                }

                if (CocheAndroid.Adaptador is { IsEnabled: true } adaptador
                    && ConexionObd.Buscar(adaptador, out _) is { } obd)
                {
                    try
                    {
                        var nueva = ConexionObd.Conectar(obd);

                        if (CocheAndroid.Preparar(nueva))
                        {
                            conexion = nueva;
                        }
                        else
                        {
                            nueva.Dispose();
                        }
                    }
                    catch (Exception)
                    {
                        // Aún dormido u ocupado: el siguiente intento.
                    }
                }
            }

            if (conexion is null)
            {
                return;
            }

            // Tres lecturas seguidas sin datos: el motor está parado.
            int fallos = 0;

            while (!parar.IsCancellationRequested && DateTime.Now < fin && fallos < 3)
            {
                fallos = CocheAndroid.LeerYGuardar(conexion) ? 0 : fallos + 1;

                if (parar.WaitHandle.WaitOne(TimeSpan.FromMinutes(2)))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // Lo normal: el OBDLink se apaga con el coche y la conexión se corta.
            RegistroErrores.Anotar(ex, "Coche.Bucle");
        }
        finally
        {
            try
            {
                conexion?.Enviar("ATD", 2000);
            }
            catch (Exception)
            {
            }

            conexion?.Dispose();

            // Las últimas lecturas, a los demás (si hay red), antes de parar.
            try
            {
                SincronizacionMobile.VaciarPendientesAsync(TimeSpan.FromSeconds(20)).Wait(TimeSpan.FromSeconds(25));
            }
            catch (Exception)
            {
            }
        }
    }

    private void Terminar(CancellationTokenSource parar)
    {
        if (ReferenceEquals(_parar, parar))
        {
            _parar = null;
        }

        parar.Dispose();

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                StopForeground(StopForegroundFlags.Remove);
            }
            else
            {
#pragma warning disable CS0618, CA1422
                StopForeground(true);
#pragma warning restore CS0618, CA1422
            }
        }
        catch (Exception)
        {
        }

        StopSelf();
    }

    private void CrearCanal()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var canal = new NotificationChannel(Canal, "Coche", NotificationImportance.Low)
        {
            Description = "Mientras se leen el combustible y los km del coche por el OBDLink."
        };

        canal.SetShowBadge(false);

        (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(canal);
    }
}

/// <summary>
/// Al conectarse el móvil al Bluetooth del coche (el elegido en Configuración),
/// arranca <see cref="ServicioCoche"/>; al desconectarse, lo para. Si Android no
/// deja arrancarlo desde segundo plano, hace una lectura suelta.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { BluetoothDevice.ActionAclConnected, BluetoothDevice.ActionAclDisconnected })]
public sealed class ReceptorCoche : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        try
        {
            string direccion = CocheAndroid.DireccionCoche;

            if (context is null || intent is null || direccion.Length == 0)
            {
                return;
            }

#pragma warning disable CS0618, CA1422
            var dispositivo = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice)?.JavaCast<BluetoothDevice>();
#pragma warning restore CS0618, CA1422

            if (!string.Equals(dispositivo?.Address, direccion, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (intent.Action == BluetoothDevice.ActionAclDisconnected)
            {
                ServicioCoche.Parar();
                return;
            }

            // Sin el permiso, Android no deja el servicio en primer plano (y cerraría la app).
            if (!CocheAndroid.PermisoConcedido || ServicioCoche.EnMarcha)
            {
                return;
            }

            try
            {
                var servicio = new Intent(context, typeof(ServicioCoche));

                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                {
                    context.StartForegroundService(servicio);
                }
                else
                {
                    context.StartService(servicio);
                }
            }
            catch (Exception)
            {
                // Android no deja arrancarlo desde segundo plano: una lectura suelta.
                PendingResult? pendiente = GoAsync();

                Task.Run(() =>
                {
                    try
                    {
                        Thread.Sleep(5000);
                        CocheAndroid.LeerUnaVez();
                    }
                    catch (Exception ex)
                    {
                        RegistroErrores.Anotar(ex, "Coche.Receptor");
                    }
                    finally
                    {
                        pendiente?.Finish();
                    }
                });
            }
        }
        catch (Exception ex)
        {
            RegistroErrores.Anotar(ex, "Coche.Receptor");
        }
    }
}
