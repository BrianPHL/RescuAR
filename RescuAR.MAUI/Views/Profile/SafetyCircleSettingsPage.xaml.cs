using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile;

public partial class SafetyCircleSettingsPage : ContentPage
{
    public SafetyCircleSettingsViewModel ViewModel { get; } = new();

    public SafetyCircleSettingsPage()
    {
        InitializeComponent();
        BindingContext = ViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ViewModel.LoadCircleDataAsync();
    }

    protected override void OnDisappearing()
    {
        ViewModel.Stop();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (ViewModel.IsDeleteModalVisible)
        {
            ViewModel.CancelDeleteCommand.Execute(null);
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
