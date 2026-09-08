using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map
{
    public partial class SafetyCirclePage : ContentPage
    {
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
            
            if (BindingContext is SafetyCircleViewModel vm)
            {
                await vm.InitializeMapAsync(MapControl);
                await vm.LoadMyCirclesAsync();
            }
        }
    }
}
