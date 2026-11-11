using Android.App;
using Android.Content;
using Android.OS;

using AndroidX.Core.App;

using SCCosmos.Mobile.Services;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Avisos meteorológicos de AEMET: una notificación por aviso (no se repite
/// aunque AEMET lo reenvíe) y una revisión cada 30 minutos en segundo plano,
/// con la app cerrada, que además refresca el tiempo del widget. Los avisos
/// rojos en congregaciones del circuito llevan «Marcar como leída».
/// </summary>
public static class TiempoAvisosAndroid
{
    private const string Canal =
        "sccosmos_avisos_tiempo";

    private const string ClaveNotificados =
        "Tiempo_AvisosNotificados";

    // Hasta la v2.1.51 la revisión iba cada 2 horas con este código.
    private const int IdAlarmaAntigua =
        4810;

    private const int IdAlarma =
        4812;

    private const int IdAbrir =
        4811;


    /// <summary>
    /// Avisos naranjas y rojos donde vas a estar (los amarillos se ven en el
    /// tiempo, sin notificación). Como los rojos del circuito: el sitio (la
    /// congregación que se visita o tu municipio) y no la zona de AEMET, que
    /// va al final del detalle.
    /// </summary>
    public static void Notificar(
        IEnumerable<(AvisoTiempo Aviso, LugarTiempo Lugar)> avisos) =>
        Publicar(
            avisos
                .Where(x => x.Aviso.Orden >= 2)
                .Select(x => (
                    x.Aviso.Clave,
                    x.Aviso.Titulo,
                    $"{Sitio(x.Lugar)} · {x.Aviso.Periodo}",
                    string.Join(
                        "\n",
                        new[] { x.Aviso.Descripcion, x.Aviso.Zona.Length > 0 ? $"(Zona de AEMET: {x.Aviso.Zona})" : "" }
                            .Where(s => s.Length > 0)),
                    x.Aviso.Fin,
                    "")));


    // «Visita a Navalmoral de la Mata» → la congregación; si no, el municipio.
    private static string Sitio(LugarTiempo lugar) =>
        lugar.Motivo.StartsWith("Visita a ", StringComparison.Ordinal)
            ? lugar.Motivo["Visita a ".Length..]
            : lugar.Nombre;


    /// <summary>
    /// Avisos rojos en congregaciones del circuito: son alertas de la app y
    /// la notificación lleva «Marcar como leída».
    /// </summary>
    public static void NotificarRojos(
        IEnumerable<TiempoAemetService.AvisoRojo> avisos) =>
        Publicar(
            avisos.Select(a => (
                a.Clave,
                a.Titulo,
                a.Texto,
                a.Descripcion,
                a.Fin,
                a.Identidad)));


    /// <summary>
    /// Quita la notificación de los avisos rojos que ya se han marcado como
    /// leídos (desde la app o el widget).
    /// </summary>
    public static void RetirarRojosLeidos(ICollection<string> pendientes)
    {
        try
        {
            NotificationManagerCompat gestor =
                NotificationManagerCompat.From(global::Android.App.Application.Context);

            foreach (TiempoAemetService.AvisoRojo aviso in TiempoAemetService.AvisosRojosVigentes())
            {
                if (!pendientes.Contains(aviso.Identidad))
                {
                    gestor.Cancel(IdDe(aviso.Clave));
                }
            }
        }
        catch
        {
        }
    }


    private static void Publicar(
        IEnumerable<(string Clave, string Titulo, string Texto, string Detalle, DateTimeOffset Fin, string Firma)> avisos)
    {
        // Como los avisos de tareas: la esposa del SC no recibe notificaciones.
        // Tampoco si las alertas meteorológicas están desactivadas (página Avisos).
        if (!PerfilAccesoService.EsHombre
            || !SCCosmos.Sincro.AjustesAvisos.Leer(clave => SCCosmos.Mobile.Data.DatabaseHelper.ObtenerConfiguracion(clave, "")).TiempoActivo)
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
                            Description = "Avisos naranjas y rojos de AEMET donde vas a estar y avisos rojos en el circuito"
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

            foreach (var aviso in avisos)
            {
                if (hechos.ContainsKey(aviso.Clave))
                {
                    continue;
                }

                hechos[aviso.Clave] =
                    aviso.Fin.UtcTicks;

                int id =
                    IdDe(aviso.Clave);

                var notificacion =
                    new NotificationCompat.Builder(contexto, Canal)
                        .SetSmallIcon(contexto.ApplicationInfo?.Icon ?? 0)
                        .SetContentTitle(aviso.Titulo)
                        .SetContentText(aviso.Texto)
                        .SetStyle(
                            new NotificationCompat.BigTextStyle()
                                .BigText(
                                    string.IsNullOrWhiteSpace(aviso.Detalle)
                                        ? aviso.Texto
                                        : $"{aviso.Texto}.\n{aviso.Detalle}"))
                        .SetPriority(NotificationCompat.PriorityHigh)
                        .SetAutoCancel(true);

                // Se quita sola cuando termina el aviso.
                long restante =
                    (long)(aviso.Fin - DateTimeOffset.Now).TotalMilliseconds;

                if (restante > 0)
                {
                    notificacion.SetTimeoutAfter(restante);
                }

                if (alPulsar is not null)
                {
                    notificacion.SetContentIntent(alPulsar);
                }

                // Los rojos del circuito son alertas de la app: se marcan como leídas.
                if (aviso.Firma.Length > 0)
                {
                    var leida =
                        new Intent(contexto, typeof(TiempoAvisoLeidoReceiver));

                    leida.PutExtra(TiempoAvisoLeidoReceiver.ExtraFirma, aviso.Firma);
                    leida.PutExtra(TiempoAvisoLeidoReceiver.ExtraId, id);

                    PendingIntent? alLeer =
                        PendingIntent.GetBroadcast(
                            contexto,
                            id,
                            leida,
                            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

                    if (alLeer is not null)
                    {
                        notificacion.AddAction(0, "Marcar como leída", alLeer);
                    }
                }

                gestor.Notify(
                    id,
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
    /// Revisión cada 30 minutos (avisos rojos del circuito, avisos donde vas a
    /// estar y tiempo del widget). Si ya está programada no se toca: volver a
    /// programarla la retrasaría.
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

            var alarmas =
                (AlarmManager?)contexto.GetSystemService(Context.AlarmService);

            // Fuera la de cada 2 horas de las versiones anteriores.
            if (PendingIntent.GetBroadcast(
                    contexto,
                    IdAlarmaAntigua,
                    intencion,
                    PendingIntentFlags.Immutable | PendingIntentFlags.NoCreate) is PendingIntent antigua)
            {
                alarmas?.Cancel(antigua);
                antigua.Cancel();
            }

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

            alarmas?.SetInexactRepeating(
                AlarmType.RtcWakeup,
                Java.Lang.JavaSystem.CurrentTimeMillis() + 2 * 60 * 1000,
                AlarmManager.IntervalHalfHour,
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


/// <summary>Revisión del tiempo cada 30 minutos, aunque la app esté cerrada.</summary>
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


/// <summary>«Marcar como leída» en la notificación de un aviso rojo del circuito.</summary>
[BroadcastReceiver(
    Enabled = true,
    Exported = false)]
public class TiempoAvisoLeidoReceiver : BroadcastReceiver
{
    public const string ExtraFirma =
        "sccosmos_firma_aviso";

    public const string ExtraId =
        "sccosmos_id_aviso";


    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        try
        {
            string firma =
                intent?.GetStringExtra(ExtraFirma) ?? "";

            if (firma.Length > 0)
            {
                AlertasMobileService.MarcarLeidaPorFirma(firma);
            }

            int id =
                intent?.GetIntExtra(ExtraId, 0) ?? 0;

            if (context is not null && id != 0)
            {
                NotificationManagerCompat.From(context).Cancel(id);
            }

            // El widget enseña las alertas pendientes.
            SccosmosWidgetProvider.ActualizarTodos();
        }
        catch
        {
        }
    }
}
