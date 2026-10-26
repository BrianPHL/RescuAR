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
        // Splash restores a verified session or explicit offline access before choosing the next screen.
        return new Window(
            serviceProvider.GetRequiredService<SplashPage>());
    }
}
