using Android.App;
using Android.Content;
using Android.OS;

using AndroidX.Core.App;

using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Avisos meteorológicos de AEMET: una notificación por aviso (no se repite
/// aunque AEMET lo reenvíe) y una revisión cada 2 horas en segundo plano, con
/// la app cerrada, que además refresca el tiempo del widget.
/// </summary>
public static class TiempoAvisosAndroid
{
    private const string Canal =
        "sccosmos_avisos_tiempo";

    private const string ClaveNotificados =
        "Tiempo_AvisosNotificados";

    private const int IdAlarma =
        4810;

    private const int IdAbrir =
        4811;


    public static void Notificar(
        IEnumerable<(AvisoTiempo Aviso, LugarTiempo Lugar)> avisos)
    {
        // Como los avisos de tareas: la esposa del SC no recibe notificaciones.
        if (!PerfilAccesoService.EsHombre)
        {
            return;
        }

        try
        {
            Context contexto =
                global::Android.App.Application.Context;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                ((NotificationManager?)contexto.GetSystemService(Context.NotificationService))
                    ?.CreateNotificationChannel(
                        new NotificationChannel(
                            Canal,
                            "Avisos meteorológicos",
                            NotificationImportance.High)
                        {
                            Description = "Avisos de AEMET donde vas a estar"
                        });
            }

            Dictionary<string, long> hechos =
                LeerNotificados();

            Intent? abrir =
                contexto.PackageManager?.GetLaunchIntentForPackage(
                    contexto.PackageName ?? "");

            PendingIntent? alPulsar =
                abrir is null
                    ? null
                    : PendingIntent.GetActivity(
                        contexto,
                        IdAbrir,
                        abrir,
                        PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

            NotificationManagerCompat gestor =
                NotificationManagerCompat.From(contexto);

            foreach ((AvisoTiempo aviso, LugarTiempo lugar) in avisos)
            {
                if (hechos.ContainsKey(aviso.Clave))
                {
                    continue;
                }

                hechos[aviso.Clave] =
                    aviso.Fin.UtcTicks;

                string texto =
                    $"{aviso.Zona}: {aviso.Periodo}";

                var notificacion =
                    new NotificationCompat.Builder(contexto, Canal)
                        .SetSmallIcon(contexto.ApplicationInfo?.Icon ?? 0)
                        .SetContentTitle($"{aviso.Titulo} · {lugar.Nombre}")
                        .SetContentText(texto)
                        .SetStyle(
                            new NotificationCompat.BigTextStyle()
                                .BigText(
                                    string.IsNullOrWhiteSpace(aviso.Descripcion)
                                        ? texto
                                        : $"{texto}.\n{aviso.Descripcion}"))
                        .SetPriority(NotificationCompat.PriorityHigh)
                        .SetAutoCancel(true);

                if (alPulsar is not null)
                {
                    notificacion.SetContentIntent(alPulsar);
                }

                gestor.Notify(
                    IdDe(aviso.Clave),
                    notificacion.Build());
            }

            GuardarNotificados(hechos);
        }
        catch
        {
            // Sin permiso de notificaciones o sin canal: no hay nada más que hacer.
        }
    }


    /// <summary>
    /// Revisión cada 2 horas (avisos y tiempo del widget). Si ya está
    /// programada no se toca: volver a programarla la retrasaría.
    /// </summary>
    public static void ProgramarRevision()
    {
        try
        {
            Context contexto =
                global::Android.App.Application.Context;

            var intencion =
                new Intent(
                    contexto,
                    typeof(TiempoAlarmReceiver));

            if (PendingIntent.GetBroadcast(
                    contexto,
                    IdAlarma,
                    intencion,
                    PendingIntentFlags.Immutable | PendingIntentFlags.NoCreate) is not null)
            {
                return;
            }

            PendingIntent? pendiente =
                PendingIntent.GetBroadcast(
                    contexto,
                    IdAlarma,
                    intencion,
                    PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

            if (pendiente is null)
            {
                return;
            }

            ((AlarmManager?)contexto.GetSystemService(Context.AlarmService))
                ?.SetInexactRepeating(
                    AlarmType.RtcWakeup,
                    Java.Lang.JavaSystem.CurrentTimeMillis() + 10 * 60 * 1000,
                    2 * AlarmManager.IntervalHour,
                    pendiente);
        }
        catch
        {
        }
    }


    // Avisos ya notificados, con su fin: se olvidan cuando terminan.
    private static Dictionary<string, long> LeerNotificados()
    {
        var hechos = new Dictionary<string, long>();
        long ahora = DateTimeOffset.UtcNow.UtcTicks;

        foreach (string entrada in Preferences.Default.Get(ClaveNotificados, "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = entrada.LastIndexOf('\t');

            if (tab > 0
                && long.TryParse(entrada[(tab + 1)..], out long fin)
                && fin > ahora)
            {
                hechos[entrada[..tab]] = fin;
            }
        }

        return hechos;
    }


    private static void GuardarNotificados(Dictionary<string, long> hechos) =>
        Preferences.Default.Set(
            ClaveNotificados,
            string.Join("\n", hechos.Select(h => h.Key + "\t" + h.Value)));


    // Id estable (GetHashCode cambia en cada arranque).
    private static int IdDe(string clave)
    {
        uint hash = 2166136261;

        foreach (char c in clave)
        {
            hash = (hash ^ c) * 16777619;
        }

        return 50000 + (int)(hash % 40000);
    }
}


/// <summary>Revisión del tiempo cada 2 horas, aunque la app esté cerrada.</summary>
[BroadcastReceiver(
    Enabled = true,
    Exported = false)]
public class TiempoAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        PendingResult? pendiente =
            GoAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                using var limite =
                    new CancellationTokenSource(TimeSpan.FromSeconds(50));

                await TiempoAemetService.ActualizarEnSegundoPlanoAsync(
                    limite.Token);

                SccosmosWidgetProvider.ActualizarTodos();
            }
            catch
            {
            }
            finally
            {
                pendiente?.Finish();
            }
        });
    }
}
