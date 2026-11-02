using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map
{
    public partial class SafetyCirclePage : ContentPage
    {
        private Window? mapWindow;
        private bool pageVisible;
        public SafetyCircleViewModel ViewModel { get; }

        public SafetyCirclePage(SafetyCircleViewModel viewModel)
        {
            ViewModel = viewModel;
            InitializeComponent();
            BindingContext = ViewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            pageVisible = true;
            mapWindow = Window;
            if (mapWindow is not null)
            {
                mapWindow.Stopped += MapWindowStopped;
                mapWindow.Resumed += MapWindowResumed;
            }

            if (BindingContext is SafetyCircleViewModel vm)
            {
                await vm.InitializeMapAsync(MapControl);
                await vm.LoadMyCirclesAsync();
            }
        }

        protected override void OnDisappearing()
        {
            pageVisible = false;
            ViewModel.DeactivateMap();
            if (mapWindow is not null)
            {
                mapWindow.Stopped -= MapWindowStopped;
                mapWindow.Resumed -= MapWindowResumed;
                mapWindow = null;
            }
            base.OnDisappearing();
        }

        private void MapWindowStopped(object? sender, EventArgs args) => ViewModel.DeactivateMap();
        private async void MapWindowResumed(object? sender, EventArgs args)
        {
            if (pageVisible) await ViewModel.InitializeMapAsync(MapControl);
        }
    }
}
