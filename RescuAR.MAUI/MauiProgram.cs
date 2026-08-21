using Microsoft.Extensions.Logging;
using RescuAR.MAUI.Evergine;
using RescuAR.MAUI.Services;
using RescuAR.MAUI.Services.Location;
using RescuAR.App.Services.Authentication;
using RescuAR.App.ViewModels.Authentication;
using RescuAR.App.Views.Authentication;

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

        // RescuAR authentication service
        builder.Services.AddSingleton<AuthenticationService>();

        // Authentication Views + ViewModels
        builder.Services.AddTransient<SplashPage>();
        builder.Services.AddTransient<SplashViewModel>();

        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<OnboardingViewModel>();

        builder.Services.AddTransient<RegistrationPage>();
        builder.Services.AddTransient<RegistrationViewModel>();

        builder.Services.AddTransient<RegistrationSuccessPage>();
        builder.Services.AddTransient<RegistrationSuccessViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<LoginViewModel>();

        builder.Services.AddTransient<GoogleAuthPage>();
        builder.Services.AddTransient<GoogleAuthViewModel>();

        builder.Services.AddTransient<PermissionsPage>();
        builder.Services.AddTransient<PermissionsViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        Services = app.Services;

        return app;
    }
}
