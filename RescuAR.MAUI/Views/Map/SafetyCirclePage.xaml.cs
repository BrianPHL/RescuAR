using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map;

public partial class SafetyCirclePage : ContentPage
{
    public SafetyCircleViewModel ViewModel { get; }
    private Window? observedWindow;
    private bool pageShown;

    public SafetyCirclePage(SafetyCircleViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        BindingContext = ViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        pageShown = true;
        observedWindow = Window;
        if (observedWindow is not null)
        {
            observedWindow.Stopped += WindowStopped;
            observedWindow.Resumed += WindowResumed;
        }
        await ViewModel.StartAsync(MapControl);
    }
    protected override void OnDisappearing()
    {
        pageShown = false;
        if (observedWindow is not null)
        {
            observedWindow.Stopped -= WindowStopped;
            observedWindow.Resumed -= WindowResumed;
            observedWindow = null;
        }
        ViewModel.Stop();
        base.OnDisappearing();
    }
    private void WindowStopped(object? sender, EventArgs args) => ViewModel.Stop();
    private async void WindowResumed(object? sender, EventArgs args)
    {
        if (pageShown) await ViewModel.StartAsync(MapControl);
    }
}
