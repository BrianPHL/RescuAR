using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile;

public partial class SystemInformationPage : ContentPage
{
    private Window? observedWindow;
    public SystemInformationPage(SystemInformationViewModel? viewModel = null)
    {
        InitializeComponent();
        BindingContext = viewModel ?? new SystemInformationViewModel();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (observedWindow is not null) observedWindow.Resumed -= OnWindowResumed;
        observedWindow = Window;
        if (observedWindow is not null) observedWindow.Resumed += OnWindowResumed;
        if (BindingContext is SystemInformationViewModel model) await model.LoadPermissionsAsync();
    }
    protected override void OnDisappearing()
    {
        if (observedWindow is not null) observedWindow.Resumed -= OnWindowResumed;
        observedWindow = null;
        base.OnDisappearing();
    }
    private async void OnWindowResumed(object? sender, EventArgs args)
    {
        if (BindingContext is SystemInformationViewModel model) await model.LoadPermissionsAsync();
    }
}
