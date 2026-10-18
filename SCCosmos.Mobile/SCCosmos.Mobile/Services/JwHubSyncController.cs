namespace SCCosmos.Mobile.Services;

public static class JwHubSyncController
{
    public static bool EstaSintonizando =>
        JwHubSyncState.Actual.EnCurso;

    public static void IniciarManual()
    {
#if ANDROID
        var context =
            Android.App.Application.Context;

        var intent =
            new Android.Content.Intent(
                context,
                typeof(
                    Platforms.Android
                        .JwHubSyncForegroundService));

        intent.SetAction(
            Platforms.Android
                .JwHubSyncForegroundService
                .AccionIniciarManual);

        if (Android.OS.Build.VERSION.SdkInt >=
            Android.OS.BuildVersionCodes.O)
        {
            context.StartForegroundService(
                intent);
        }
        else
        {
            context.StartService(
                intent);
        }
#endif
    }

    public static void IniciarAutomatico()
    {
#if ANDROID
        var context =
            Android.App.Application.Context;

        var intent =
            new Android.Content.Intent(
                context,
                typeof(
                    Platforms.Android
                        .JwHubSyncForegroundService));

        intent.SetAction(
            Platforms.Android
                .JwHubSyncForegroundService
                .AccionIniciarAutomatico);

        if (Android.OS.Build.VERSION.SdkInt >=
            Android.OS.BuildVersionCodes.O)
        {
            context.StartForegroundService(
                intent);
        }
        else
        {
            context.StartService(
                intent);
        }
#endif
    }

    public static void Cancelar()
    {
#if ANDROID
        var context =
            Android.App.Application.Context;

        var intent =
            new Android.Content.Intent(
                context,
                typeof(
                    Platforms.Android
                        .JwHubSyncForegroundService));

        intent.SetAction(
            Platforms.Android
                .JwHubSyncForegroundService
                .AccionCancelar);

        context.StartService(
            intent);
#endif
    }
}