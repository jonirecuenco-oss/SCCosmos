using CommunityToolkit.Maui;

namespace SCCosmos.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                // Que se vean «Cancelar» y «Aceptar» en los diálogos de fecha y hora.
                handlers.AddHandler<TimePicker, Platforms.Android.SelectorHoraHandler>();
                handlers.AddHandler<DatePicker, Platforms.Android.SelectorFechaHandler>();
#endif
            });

        return builder.Build();
    }
}