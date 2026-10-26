using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map
{
    public partial class MapPage : ContentPage
    {
        private Window? observedWindow;
        private bool pageShown;
        public MapPage()
        {
            InitializeComponent();
            Title = "General / Offline Map";
            Shell.SetNavBarIsVisible(this, true);
            BindingContext = new MapViewModel();
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

            if (BindingContext is ViewModels.Map.MapViewModel vm)
            {
                await vm.InitializeMapAsync(MapControl);
            }
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
            if (BindingContext is MapViewModel vm) vm.Stop();
            base.OnDisappearing();
        }
        private void WindowStopped(object? sender, EventArgs args) { if (BindingContext is MapViewModel vm) vm.Stop(); }
        private async void WindowResumed(object? sender, EventArgs args)
        {
            if (pageShown && BindingContext is MapViewModel vm) await vm.InitializeMapAsync(MapControl);
        }
    }
}
