using Microsoft.Extensions.Logging;
using RescuAR.App.Services.Unity;


#if ANDROID
using RescuAR.App.Platforms.Android.Unity;
#endif

namespace RescuAR.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if ANDROID
            builder.Services.AddSingleton<IUnityService, UnityService>();
#endif
            builder.Services.AddSingleton<RescuAR.App.Services.Navigation.OSRMService>();
            builder.Services.AddSingleton<RescuAR.App.Services.Navigation.OfflineRoutingService>();
            builder.Services.AddSingleton<RescuAR.App.Services.Navigation.RoutingService>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
