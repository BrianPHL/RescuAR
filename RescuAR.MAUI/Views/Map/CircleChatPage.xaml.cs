using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map;

public partial class CircleChatPage : ContentPage
{
    public CircleChatViewModel ViewModel { get; }
    private Window? observedWindow;
    private bool pageShown;
    private bool nearMessageEnd = true;

    public CircleChatPage(CircleChatViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        BindingContext = ViewModel;
        ViewModel.MessageAdded += MessageAdded;
    }
    private void MessagesScrolled(object? sender, ItemsViewScrolledEventArgs args) =>
        nearMessageEnd = args.LastVisibleItemIndex >= ViewModel.Messages.Count - 2;
    private void MessageAdded(ChatMessageItem item)
    {
        bool follow = nearMessageEnd || item.IsMyMessage;
        if (!follow) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!pageShown || !ViewModel.Messages.Contains(item)) return;
            try { MessagesCollectionView.ScrollTo(item, position: ScrollToPosition.End, animate: true); }
            catch { }
        });
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); pageShown = true; nearMessageEnd = true;
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
