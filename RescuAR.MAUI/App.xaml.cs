using RescuAR.App.Views.Authentication;
using Microsoft.Extensions.DependencyInjection;
namespace RescuAR.MAUI;

public partial class App : Application
{
    private readonly IServiceProvider serviceProvider;

    public App(
        IServiceProvider serviceProvider)
    {
        InitializeComponent();

        this.serviceProvider =
            serviceProvider;
    }

    protected override Window CreateWindow(
        IActivationState? activationState)
    {
        /*
         * SplashViewModel owns the updated source startup decision:
         * - logged in + permissions complete -> AppShell
         * - logged in + permissions incomplete -> PermissionsPage
         * - signed up but logged out -> LoginPage
         * - first run -> OnboardingPage
         */
        return new Window(
            serviceProvider.GetRequiredService<SplashPage>());
    }
}
