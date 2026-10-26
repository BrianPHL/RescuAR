using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile;

public partial class AppSettingsPage : ContentPage
{
    private Window? observedWindow;
    public AppSettingsPage()
    {
        InitializeComponent();
        BindingContext = new AppSettingsViewModel();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (observedWindow is not null) observedWindow.Resumed -= OnResumed;
        observedWindow = Window;
        if (observedWindow is not null) observedWindow.Resumed += OnResumed;
        if (BindingContext is AppSettingsViewModel model) await model.InitializeSettingsAsync();
    }
    protected override void OnDisappearing()
    {
        if (observedWindow is not null) observedWindow.Resumed -= OnResumed;
        observedWindow = null;
        base.OnDisappearing();
    }
    private async void OnResumed(object? sender, EventArgs args)
    {
        if (BindingContext is AppSettingsViewModel model) await model.InitializeSettingsAsync();
    }
}
