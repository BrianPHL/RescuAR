using RescuAR.App.Views.Authentication;

namespace RescuAR.MAUI;

public partial class App : Application
{
    private readonly SplashPage splashPage;

    public App(
        SplashPage splashPage)
    {
        InitializeComponent();

        this.splashPage =
            splashPage;
    }

    protected override Window CreateWindow(
        IActivationState? activationState)
    {
        /*
         * Keep the .NET 9 Window-based application architecture.
         *
         * SplashViewModel owns the updated source startup decision:
         * - logged in + permissions complete -> AppShell
         * - logged in + permissions incomplete -> PermissionsPage
         * - signed up but logged out -> LoginPage
         * - first run -> OnboardingPage
         */
        return new Window(
            splashPage);
    }
}
