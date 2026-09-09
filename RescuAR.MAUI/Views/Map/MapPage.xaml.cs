using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Map;

namespace RescuAR.App.Views.Map
{
    public partial class MapPage : ContentPage
    {
        public MapPage()
        {
            InitializeComponent();
            BindingContext = new MapViewModel();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            
            if (BindingContext is ViewModels.Map.MapViewModel vm)
            {
                await vm.InitializeMapAsync(MapControl);
            }
        }
    }
}
