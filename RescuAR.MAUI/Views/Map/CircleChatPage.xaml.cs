using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map;

public partial class CircleChatPage : ContentPage
{
    public CircleChatViewModel ViewModel { get; }
    private Window? observedWindow;
    private bool pageShown;

    public CircleChatPage(CircleChatViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        BindingContext = ViewModel;
        ViewModel.MessageAdded += MessageAdded;
    }
    private void MessageAdded(ChatMessageItem item) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!pageShown || !ViewModel.Messages.Contains(item)) return;
        try { MessagesCollectionView.ScrollTo(item, position: ScrollToPosition.End, animate: true); }
        catch { }
    });
    protected override async void OnAppearing()
    {
        base.OnAppearing(); pageShown = true;
        observedWindow = Window;
        if (observedWindow is not null) { observedWindow.Stopped += WindowStopped; observedWindow.Resumed += WindowResumed; }
        await ViewModel.StartAsync();
    }
    protected override void OnDisappearing()
    {
        pageShown = false;
        if (observedWindow is not null) { observedWindow.Stopped -= WindowStopped; observedWindow.Resumed -= WindowResumed; observedWindow = null; }
        ViewModel.Stop(); base.OnDisappearing();
    }
    private void WindowStopped(object? sender, EventArgs args) => ViewModel.Suspend();
    private async void WindowResumed(object? sender, EventArgs args) { if (pageShown) await ViewModel.ResumeAsync(); }
}
