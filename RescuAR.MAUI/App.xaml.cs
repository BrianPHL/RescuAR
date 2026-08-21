using Microsoft.Maui.Storage;

using RescuAR.App.Views.Authentication;

namespace RescuAR.MAUI;

public partial class App : Application
{
    private readonly OnboardingPage onboardingPage;

    public App(
        OnboardingPage onboardingPage)
    {
        InitializeComponent();

        this.onboardingPage =
            onboardingPage;
    }

    protected override Window CreateWindow(
        IActivationState? activationState)
    {
        bool isLoggedIn =
            Preferences.Default.Get(
                "IsLoggedIn",
                false);

        Page rootPage =
            isLoggedIn
                ? new AppShell()
                : onboardingPage;

        return new Window(
            rootPage);
    }
}
