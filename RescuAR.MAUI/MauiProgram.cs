using Microsoft.Extensions.Logging;
using RescuAR.MAUI.Evergine;
using RescuAR.MAUI.Services;
using RescuAR.MAUI.Services.Location;

namespace RescuAR.MAUI;

public static class MauiProgram
{
    public static IServiceProvider Services
    {
        get;
        private set;
    } = default!;
    public static MauiApp CreateMauiApp()
    {
        var builder =
            MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiEvergine()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont(
                    "OpenSans-Regular.ttf",
                    "OpenSansRegular");

                fonts.AddFont(
                    "OpenSans-Semibold.ttf",
                    "OpenSansSemibold");
            });

#if ANDROID

        builder.Services.AddSingleton<
            IArCoreService,
            Platforms.Android.Services.ArCoreService>();

        builder.Services.AddSingleton<
            ILocationService,
            MauiLocationService>();

#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        Services = app.Services;

        return app;
    }
}
