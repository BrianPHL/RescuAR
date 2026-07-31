// using RescuAR.App.Services.Unity;
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
        // var unityService = Handler?.MauiContext?.Services.GetService<IUnityService>();
        // unityService?.LaunchUnity();
#endif
    }

    private async void OnHelpCenterClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(Profile.HelpCenterPage));
    }

    private async void OnPrivacyPolicyClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(Profile.PrivacyPolicyPage));
    }

    private async void OnTermsConditionsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(Profile.TermsConditionsPage));
    }

    private async void OnSystemInformationClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(Profile.SystemInformationPage));
    }

    private async void OnSummaryClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(Views.Summary.SummaryPage));
    }
} 
