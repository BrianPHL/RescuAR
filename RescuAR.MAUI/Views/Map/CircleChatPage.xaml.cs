using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map;

public partial class CircleChatPage : ContentPage
{
    public CircleChatPage(CircleChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        viewModel.MessageAdded += (item) =>
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MessagesCollectionView.ScrollTo(item, position: ScrollToPosition.End, animate: true);
                });
            }
            catch { }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is CircleChatViewModel vm)
        {
            await vm.LoadMessagesAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is CircleChatViewModel vm)
        {
            vm.StopTimer();
        }
    }
}
