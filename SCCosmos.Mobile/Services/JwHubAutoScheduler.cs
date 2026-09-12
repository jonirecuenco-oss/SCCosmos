namespace SCCosmos.Mobile.Services;

public static class JwHubAutoScheduler
{
#if ANDROID

    private const int RequestCode = 7410;

    private static Android.App.PendingIntent
        ObtenerPendingIntent()
    {
        var context =
            Android.App.Application.Context;

        var intent =
            new Android.Content.Intent(
                context,
                typeof(
                    Platforms.Android
                        .JwHubAlarmReceiver));

        var flags =
            Android.App.PendingIntentFlags.UpdateCurrent
            |
            Android.App.PendingIntentFlags.Immutable;

        return Android.App.PendingIntent
            .GetBroadcast(
                context,
                RequestCode,
                intent,
                flags)!;
    }

#endif

    public static bool PuedeProgramarExactamente
    {
        get
        {
#if ANDROID

            if (Android.OS.Build.VERSION.SdkInt <
                Android.OS.BuildVersionCodes.S)
            {
                return true;
            }

            var context =
                Android.App.Application.Context;

            var alarmManager =
                (Android.App.AlarmManager?)
                context.GetSystemService(
                    Android.Content.Context.AlarmService);

            return alarmManager
                       ?.CanScheduleExactAlarms()
                   ?? false;

#else
            return false;
#endif
        }
    }

    public static void ProgramarSiguiente()
    {
#if ANDROID
        if (!JwHubAutoSettings.Activo)
        {
            Cancelar();
            return;
        }

        if (!PuedeProgramarExactamente)
            return;

        var context =
            Android.App.Application.Context;

        var alarmManager =
            (Android.App.AlarmManager?)
            context.GetSystemService(
                Android.Content.Context.AlarmService);

        if (alarmManager == null)
            return;

        DateTime ahora =
            DateTime.Now;

        DateTime siguiente =
            ahora.Date.Add(
                JwHubAutoSettings.Hora);

        if (siguiente <= ahora)
        {
            siguiente =
                siguiente.AddDays(1);
        }

        long momento =
            new DateTimeOffset(
                siguiente)
            .ToUnixTimeMilliseconds();

        alarmManager.SetExactAndAllowWhileIdle(
            Android.App.AlarmType.RtcWakeup,
            momento,
            ObtenerPendingIntent());
#endif
    }

    public static void Cancelar()
    {
#if ANDROID
        var context =
            Android.App.Application.Context;

        var alarmManager =
            (Android.App.AlarmManager?)
            context.GetSystemService(
                Android.Content.Context.AlarmService);

        alarmManager?.Cancel(
            ObtenerPendingIntent());
#endif
    }

    public static void SolicitarPermiso()
    {
#if ANDROID
        if (PuedeProgramarExactamente)
            return;

        var activity =
            Microsoft.Maui.ApplicationModel
                .Platform.CurrentActivity;

        if (activity == null)
            return;

        var intent =
            new Android.Content.Intent(
                Android.Provider.Settings
                    .ActionRequestScheduleExactAlarm);

        intent.SetData(
            Android.Net.Uri.Parse(
                $"package:{activity.PackageName}"));

        activity.StartActivity(
            intent);
#endif
    }
}