using RescuAR.App.Services.Unity;
namespace RescuAR.App.Views;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private void LaunchUnityClicked(
        object? sender,
        EventArgs e)
    {
#if ANDROID
        var unityService =
            Handler?.MauiContext?.Services.GetService<IUnityService>();

        unityService?.LaunchUnity();
#endif
    }
} 
