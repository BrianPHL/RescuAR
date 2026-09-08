using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map
{
    public partial class SafetyCirclePage : ContentPage
    {
        public SafetyCirclePage(SafetyCircleViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            
            if (BindingContext is SafetyCircleViewModel vm)
            {
                await vm.InitializeMapAsync(MapControl);
                await vm.LoadMyCirclesAsync();
            }
        }
    }
}
